import { CommonModule, JsonPipe } from '@angular/common';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

type MessageRole = 'user' | 'assistant';

type StatusLevel = 'idle' | 'busy' | 'success' | 'warning' | 'error';

interface Citation {
  claimId?: string;
  evidenceId?: string;
  sourceType?: string;
  sourceRef?: string;
  excerptOrSummary?: string;
}

interface ContradictionItem {
  contradictionId?: string;
  type?: string;
  severity?: string;
  status?: string;
}

interface AnswerConscience {
  decision?: string;
  riskScore?: number;
  policyVersion?: string;
  reasonCodes?: string[];
}

interface ToolExecution {
  toolName?: string;
  source?: string;
  ok?: boolean;
  code?: string;
  message?: string;
  idempotencyKey?: string;
  traceId?: string;
  data?: unknown;
  eventIds?: string[];
  resultCount?: number;
}

interface OpenAiChatMetadata {
  confidence?: number;
  uncertaintyFlags?: string[];
  contradictions?: ContradictionItem[];
  citations?: Citation[];
  conscience?: AnswerConscience;
  toolExecutions?: ToolExecution[];
}

interface OpenAiUsage {
  promptTokens?: number;
  completionTokens?: number;
  totalTokens?: number;
}

interface MessageDiagnostics {
  completionId?: string;
  model?: string;
  created?: number;
  finishReason?: string | null;
  streamMode: boolean;
  latencyMs?: number;
  usage?: OpenAiUsage;
}

interface UiMessage {
  role: MessageRole;
  content: string;
  metadata?: OpenAiChatMetadata;
  diagnostics?: MessageDiagnostics;
  rawMetadataJson?: string;
}

interface OpenAiModelListResponse {
  data?: Array<{ id: string }>;
}

interface OpenAiChatCompletionResponse {
  id?: string;
  model?: string;
  created?: number;
  usage?: OpenAiUsage;
  choices?: Array<{
    message?: {
      role?: string;
      content?: string;
      metadata?: OpenAiChatMetadata;
    };
    finishReason?: string;
    finish_reason?: string;
  }>;
}

interface OpenAiChatCompletionChunk {
  id?: string;
  model?: string;
  created?: number;
  usage?: OpenAiUsage;
  choices?: Array<{
    delta?: {
      role?: string;
      content?: string;
      metadata?: OpenAiChatMetadata;
    };
    finishReason?: string | null;
    finish_reason?: string | null;
  }>;
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule, JsonPipe],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly chatUserKey = this.getOrCreateChatUserKey();

  apiBaseUrl = '/v1';
  apiKey = 'local-dev-key';

  models: string[] = [];
  selectedModel = 'cognitivememory-chat';

  messages: UiMessage[] = [];
  userInput = '';

  loadingModels = false;
  sending = false;
  statusLevel: StatusLevel = 'idle';
  status = 'Ready.';
  selectedMessageIndex: number | null = null;

  ngOnInit(): void {
    void this.refreshModels();
  }

  get selectedAssistantMessage(): UiMessage | null {
    if (this.selectedMessageIndex !== null) {
      const selected = this.messages[this.selectedMessageIndex];
      if (selected && selected.role === 'assistant') {
        return selected;
      }
    }

    for (let i = this.messages.length - 1; i >= 0; i -= 1) {
      if (this.messages[i].role === 'assistant') {
        return this.messages[i];
      }
    }

    return null;
  }

  get selectedToolExecutions(): ToolExecution[] {
    return this.selectedAssistantMessage?.metadata?.toolExecutions ?? [];
  }

  get selectedCitations(): Citation[] {
    return this.selectedAssistantMessage?.metadata?.citations ?? [];
  }

  get selectedContradictions(): ContradictionItem[] {
    return this.selectedAssistantMessage?.metadata?.contradictions ?? [];
  }

  get selectedUncertaintyFlags(): string[] {
    return this.selectedAssistantMessage?.metadata?.uncertaintyFlags ?? [];
  }

  get selectedReasonCodes(): string[] {
    return this.selectedAssistantMessage?.metadata?.conscience?.reasonCodes ?? [];
  }

  async refreshModels(): Promise<void> {
    this.loadingModels = true;
    this.setStatus('busy', 'Loading models...');

    try {
      const response = await firstValueFrom(
        this.http.get<OpenAiModelListResponse>(`${this.normalizeBaseUrl()}/models`, {
          headers: this.buildHeaders(),
        }),
      );

      const fetchedModels = (response.data ?? [])
        .map((model) => model.id)
        .filter((id) => id && id.trim().length > 0);

      this.models =
        fetchedModels.length > 0 ? Array.from(new Set(fetchedModels)) : [this.selectedModel];

      if (!this.models.includes(this.selectedModel)) {
        this.selectedModel = this.models[0];
      }

      this.setStatus('success', `Loaded ${this.models.length} model(s).`);
    } catch (error) {
      this.models = [this.selectedModel];
      this.setStatus('error', `Model load failed: ${this.extractError(error)}`);
    } finally {
      this.loadingModels = false;
      this.cdr.detectChanges();
    }
  }

  async send(): Promise<void> {
    const prompt = this.userInput.trim();
    if (!prompt || this.sending) {
      return;
    }

    this.userInput = '';
    this.messages = [...this.messages, { role: 'user', content: prompt }];
    this.sending = true;
    this.setStatus('busy', 'Streaming response...');

    const conversationMessages = this.messages.map((message) => ({
      role: message.role,
      content: message.content,
    }));

    const payload = {
      model: this.selectedModel,
      stream: true,
      user: this.chatUserKey,
      messages: conversationMessages,
    };

    const assistantMessage: UiMessage = {
      role: 'assistant',
      content: '',
      diagnostics: {
        streamMode: true,
      },
    };

    const startedAt = Date.now();
    this.messages = [...this.messages, assistantMessage];
    this.selectedMessageIndex = this.messages.length - 1;
    this.cdr.detectChanges();

    try {
      await this.sendStreamingCompletion(payload, assistantMessage);
      assistantMessage.content = assistantMessage.content.trim() || 'No response text returned.';
      assistantMessage.diagnostics = {
        ...(assistantMessage.diagnostics ?? { streamMode: true }),
        latencyMs: Math.max(1, Date.now() - startedAt),
      };
      this.setStatus('success', 'Response received.');
    } catch (error) {
      const friendlyError = this.extractError(error);
      assistantMessage.content = `Request failed: ${friendlyError}`;
      assistantMessage.metadata = undefined;
      assistantMessage.rawMetadataJson = undefined;
      this.setStatus('error', `Request failed: ${friendlyError}`);
    } finally {
      this.sending = false;
      this.cdr.detectChanges();
    }
  }

  clearChat(): void {
    this.messages = [];
    this.selectedMessageIndex = null;
    this.setStatus('idle', 'Chat cleared.');
  }

  selectMessage(index: number): void {
    if (!this.messages[index] || this.messages[index].role !== 'assistant') {
      return;
    }

    this.selectedMessageIndex = index;
  }

  isSelected(index: number): boolean {
    return this.selectedMessageIndex === index;
  }

  normalizeConfidence(value: number | undefined): number {
    if (value === undefined || Number.isNaN(value)) {
      return 0;
    }

    return Math.max(0, Math.min(100, Math.round(value * 100)));
  }

  usageTotal(message: UiMessage | null): number {
    const usage = message?.diagnostics?.usage;
    if (!usage) {
      return 0;
    }

    return usage.totalTokens ?? (usage.promptTokens ?? 0) + (usage.completionTokens ?? 0);
  }

  hasAnyApiSignals(message: UiMessage | null): boolean {
    if (!message) {
      return false;
    }

    return Boolean(
      message.metadata ||
      message.diagnostics?.completionId ||
      message.diagnostics?.usage ||
      message.diagnostics?.finishReason,
    );
  }

  formatCreatedAt(unixSeconds: number | undefined): string {
    if (!unixSeconds) {
      return 'n/a';
    }

    try {
      return new Date(unixSeconds * 1000).toLocaleString();
    } catch {
      return 'n/a';
    }
  }

  getStatusClasses(): Record<string, boolean> {
    return {
      'border-slate-700 bg-slate-900/70 text-slate-200': this.statusLevel === 'idle',
      'border-sky-500/40 bg-sky-500/10 text-sky-200': this.statusLevel === 'busy',
      'border-emerald-500/40 bg-emerald-500/10 text-emerald-200': this.statusLevel === 'success',
      'border-amber-500/40 bg-amber-500/10 text-amber-200': this.statusLevel === 'warning',
      'border-rose-500/40 bg-rose-500/10 text-rose-200': this.statusLevel === 'error',
    };
  }

  getToolBadgeClasses(ok: boolean | undefined): Record<string, boolean> {
    return {
      'border-emerald-500/40 bg-emerald-500/10 text-emerald-300': ok === true,
      'border-rose-500/40 bg-rose-500/10 text-rose-300': ok === false,
      'border-slate-500/40 bg-slate-500/10 text-slate-300': ok === undefined,
    };
  }

  toolStatusText(ok: boolean | undefined): string {
    if (ok === true) {
      return 'ok';
    }

    if (ok === false) {
      return 'failed';
    }

    return 'unknown';
  }

  hasToolData(tool: ToolExecution): boolean {
    return tool.data !== undefined && tool.data !== null;
  }

  trackByIndex(index: number): number {
    return index;
  }

  private normalizeBaseUrl(): string {
    const baseUrl = this.apiBaseUrl.trim();
    if (!baseUrl) {
      return '/v1';
    }

    return baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;
  }

  private buildHeaders(): HttpHeaders {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
    };

    const apiKey = this.apiKey.trim();
    if (apiKey.length > 0) {
      headers['Authorization'] = `Bearer ${apiKey}`;
    }

    return new HttpHeaders(headers);
  }

  private buildFetchHeaders(): Record<string, string> {
    const headers: Record<string, string> = {
      Accept: 'text/event-stream',
      'Content-Type': 'application/json',
    };

    const apiKey = this.apiKey.trim();
    if (apiKey.length > 0) {
      headers['Authorization'] = `Bearer ${apiKey}`;
    }

    return headers;
  }

  private async sendStreamingCompletion(
    payload: object,
    assistantMessage: UiMessage,
  ): Promise<void> {
    const response = await fetch(`${this.normalizeBaseUrl()}/chat/completions`, {
      method: 'POST',
      headers: this.buildFetchHeaders(),
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      throw new Error(await this.extractFetchError(response));
    }

    const contentType = response.headers.get('content-type') ?? '';
    if (contentType.includes('application/json')) {
      const completion = (await response.json()) as OpenAiChatCompletionResponse;
      this.applyJsonCompletion(completion, assistantMessage);
      return;
    }

    if (!response.body) {
      throw new Error('No streaming body was returned by the API.');
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
      const { value, done } = await reader.read();
      if (done) {
        break;
      }

      buffer += decoder.decode(value, { stream: true });

      let separatorIndex = buffer.indexOf('\n\n');
      while (separatorIndex >= 0) {
        const frame = buffer.slice(0, separatorIndex);
        buffer = buffer.slice(separatorIndex + 2);
        this.applySseFrame(frame, assistantMessage);
        separatorIndex = buffer.indexOf('\n\n');
      }

      this.cdr.detectChanges();
    }

    buffer += decoder.decode();
    if (buffer.trim().length > 0) {
      this.applySseFrame(buffer, assistantMessage);
    }
  }

  private applyJsonCompletion(
    completion: OpenAiChatCompletionResponse,
    assistantMessage: UiMessage,
  ): void {
    const firstChoice = completion.choices?.[0];
    const message = firstChoice?.message;

    if (message?.content) {
      assistantMessage.content += message.content;
    }

    this.applyMetadata(assistantMessage, message?.metadata);

    assistantMessage.diagnostics = {
      ...(assistantMessage.diagnostics ?? { streamMode: false }),
      streamMode: false,
      completionId: completion.id,
      model: completion.model,
      created: completion.created,
      finishReason: firstChoice?.finishReason ?? firstChoice?.finish_reason ?? null,
      usage: completion.usage,
    };
  }

  private applySseFrame(frame: string, assistantMessage: UiMessage): void {
    if (!frame || frame.startsWith(':')) {
      return;
    }

    const data = frame
      .split(/\r?\n/)
      .map((line) => line.replace(/^\uFEFF/, ''))
      .filter((line) => line.startsWith('data:'))
      .map((line) => line.slice(5).trimStart())
      .join('\n');

    if (!data || data === '[DONE]') {
      return;
    }

    let chunk: OpenAiChatCompletionChunk;
    try {
      chunk = JSON.parse(data) as OpenAiChatCompletionChunk;
    } catch {
      return;
    }

    const firstChoice = chunk.choices?.[0];
    const delta = firstChoice?.delta;

    if (delta?.content) {
      assistantMessage.content += delta.content;
    }

    this.applyMetadata(assistantMessage, delta?.metadata);

    assistantMessage.diagnostics = {
      ...(assistantMessage.diagnostics ?? { streamMode: true }),
      streamMode: true,
      completionId: chunk.id ?? assistantMessage.diagnostics?.completionId,
      model: chunk.model ?? assistantMessage.diagnostics?.model,
      created: chunk.created ?? assistantMessage.diagnostics?.created,
      finishReason:
        firstChoice?.finishReason ??
        firstChoice?.finish_reason ??
        assistantMessage.diagnostics?.finishReason,
      usage: chunk.usage ?? assistantMessage.diagnostics?.usage,
      latencyMs: assistantMessage.diagnostics?.latencyMs,
    };
  }

  private applyMetadata(message: UiMessage, metadata: OpenAiChatMetadata | undefined): void {
    if (!metadata) {
      return;
    }

    message.metadata = metadata;
    message.rawMetadataJson = JSON.stringify(metadata, null, 2);
  }

  private setStatus(level: StatusLevel, text: string): void {
    this.statusLevel = level;
    this.status = text;
  }

  private async extractFetchError(response: Response): Promise<string> {
    const text = await response.text();
    if (!text) {
      return `${response.status} ${response.statusText}`.trim();
    }

    try {
      const parsed = JSON.parse(text) as { error?: { message?: string } };
      const message = parsed.error?.message ?? text;
      return `${response.status} ${message}`.trim();
    } catch {
      return `${response.status} ${text}`.trim();
    }
  }

  private extractError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      const apiMessage =
        (error.error as { error?: { message?: string } } | null)?.error?.message ?? error.message;
      return `${error.status} ${apiMessage}`.trim();
    }

    if (error instanceof Error) {
      return error.message;
    }

    return 'Unknown error.';
  }

  private getOrCreateChatUserKey(): string {
    const storageKey = 'cm.chat_user_key';
    const generated = `webchat-${this.generateClientId()}`;

    try {
      const existing = globalThis.localStorage?.getItem(storageKey)?.trim();
      if (existing && existing.length > 0) {
        return existing;
      }

      globalThis.localStorage?.setItem(storageKey, generated);
      return generated;
    } catch {
      return generated;
    }
  }

  private generateClientId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }

    return `${Date.now()}-${Math.random().toString(16).slice(2, 10)}`;
  }
}
