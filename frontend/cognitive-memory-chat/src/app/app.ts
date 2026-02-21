import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { marked } from 'marked';
import { firstValueFrom } from 'rxjs';

type Role = 'user' | 'assistant';

type StatusTone = 'idle' | 'busy' | 'ok' | 'error';
type ConsolePage = 'overview' | 'chat' | 'analytics' | 'operations';

interface PageNavItem {
  id: ConsolePage;
  label: string;
  caption: string;
}

interface ChatTurn {
  role: Role;
  text: string;
  startedAtUtc?: string;
  completedAtUtc?: string;
  toolCalls?: ToolInvocationAudit[];
  memoryLayers?: string[];
}

interface ParsedAssistantPayload {
  stages: string[];
  finalAnswer: string;
}

interface CachedParsedPayload {
  sourceText: string;
  payload: ParsedAssistantPayload;
}

interface StreamChunk {
  sessionId: string;
  delta: string;
  isFinal: boolean;
  generatedAtUtc: string;
  contextTurnCount: number;
}

interface RawStreamChunk {
  sessionId?: string;
  delta?: string;
  isFinal?: boolean;
  generatedAtUtc?: string;
  contextTurnCount?: number;
  SessionId?: string;
  Delta?: string;
  IsFinal?: boolean;
  GeneratedAtUtc?: string;
  ContextTurnCount?: number;
  message?: string;
  detail?: string;
}

interface SelfPreference {
  key: string;
  value: string;
  updatedAtUtc: string;
}

interface SelfModelSnapshot {
  preferences: SelfPreference[];
}

interface ToolInvocationAudit {
  auditId: string;
  toolName: string;
  isWrite: boolean;
  argumentsJson: string;
  resultJson: string;
  succeeded: boolean;
  error?: string | null;
  executedAtUtc: string;
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly http = inject(HttpClient);
  private readonly cdr = inject(ChangeDetectorRef);
  private parsedPayloadCache = new WeakMap<ChatTurn, CachedParsedPayload>();
  private readonly markdownCache = new Map<string, string>();
  private readonly inlineMarkdownCache = new Map<string, string>();
  private readonly markdownCacheLimit = 400;
  readonly navigation: PageNavItem[] = [
    { id: 'overview', label: 'Overview', caption: 'Mission control' },
    { id: 'chat', label: 'Chat Studio', caption: 'Live streaming' },
    { id: 'analytics', label: 'Analytics', caption: 'Evidence telemetry' },
    { id: 'operations', label: 'Operations', caption: 'Memory runtime' },
  ];

  apiBaseUrl = '/api';
  messageInput = '';
  sessionId = this.getOrCreateSessionId();
  turns: ChatTurn[] = [];
  thinkingExpandedByTurn: Record<number, boolean> = {};
  activePage: ConsolePage = 'overview';

  streaming = false;
  statusTone: StatusTone = 'idle';
  statusText = 'Ready.';

  episodicTake = 20;
  episodicEvents: unknown[] = [];

  claimSubject = '';
  claimPredicate = '';
  claimValue = '';
  claimConfidence = 0.7;
  semanticSubject = '';
  semanticPredicate = '';
  semanticResults: unknown[] = [];

  routineTrigger = '';
  routineName = '';
  routineSteps = '';
  routineOutcome = '';

  prefKey = '';
  prefValue = '';
  selfSnapshot: SelfModelSnapshot | null = null;

  consolidationResult: unknown = null;
  decayResult: unknown = null;

  async sendStream(): Promise<void> {
    const input = this.messageInput.trim();
    if (!input || this.streaming) {
      return;
    }

    this.streaming = true;
    this.setStatus('busy', 'Streaming response...');
    this.messageInput = '';

    const startedAtUtc = new Date().toISOString();
    this.turns = [...this.turns, { role: 'user', text: input }, { role: 'assistant', text: '', startedAtUtc }];
    const assistantIndex = this.turns.length - 1;

    try {
      const response = await fetch(`${this.baseUrl()}/chat/stream`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'text/event-stream',
        },
        body: JSON.stringify({ message: input, sessionId: this.sessionId }),
      });

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${await response.text()}`);
      }

      if (!response.body) {
        throw new Error('No stream body returned.');
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
        buffer = this.processSseBuffer(buffer, assistantIndex);
        this.cdr.detectChanges();
      }

      buffer += decoder.decode();
      if (buffer.trim().length > 0) {
        this.processSseFrame(buffer, assistantIndex);
      }

      const assistant = this.turns[assistantIndex];
      assistant.completedAtUtc = new Date().toISOString();
      if (assistant.text.trim().length === 0) {
        assistant.text = '[No response text returned]';
      }
      await this.loadToolEvidenceForTurn(assistantIndex);
      this.setStatus('ok', 'Response streamed.');
    } catch (error) {
      this.turns[assistantIndex].text = `Request failed: ${this.toError(error)}`;
      this.setStatus('error', this.toError(error));
    } finally {
      this.streaming = false;
      this.cdr.detectChanges();
    }
  }

  async loadEpisodic(): Promise<void> {
    this.setStatus('busy', 'Loading episodic timeline...');
    try {
      const path = `${this.baseUrl()}/episodic/events/${encodeURIComponent(this.sessionId)}?take=${this.episodicTake}`;
      const data = await firstValueFrom(this.http.get<unknown[]>(path));
      this.episodicEvents = data;
      this.setStatus('ok', `Loaded ${data.length} episodic event(s).`);
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async createClaim(): Promise<void> {
    this.setStatus('busy', 'Creating semantic claim...');
    try {
      await firstValueFrom(
        this.http.post(`${this.baseUrl()}/semantic/claims`, {
          subject: this.claimSubject,
          predicate: this.claimPredicate,
          value: this.claimValue,
          confidence: this.claimConfidence,
          scope: 'global',
        }),
      );
      this.setStatus('ok', 'Semantic claim created.');
      await this.queryClaims();
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async queryClaims(): Promise<void> {
    this.setStatus('busy', 'Querying semantic claims...');
    try {
      const query = new URLSearchParams();
      if (this.semanticSubject.trim()) {
        query.set('subject', this.semanticSubject.trim());
      }
      if (this.semanticPredicate.trim()) {
        query.set('predicate', this.semanticPredicate.trim());
      }
      query.set('take', '40');

      const data = await firstValueFrom(this.http.get<unknown[]>(`${this.baseUrl()}/semantic/claims?${query.toString()}`));
      this.semanticResults = data;
      this.setStatus('ok', `Loaded ${data.length} claim(s).`);
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async upsertRoutine(): Promise<void> {
    this.setStatus('busy', 'Saving procedural routine...');
    try {
      const steps = this.routineSteps
        .split('\n')
        .map((x) => x.trim())
        .filter((x) => x.length > 0);

      await firstValueFrom(
        this.http.post(`${this.baseUrl()}/procedural/routines`, {
          routineId: null,
          trigger: this.routineTrigger,
          name: this.routineName,
          steps,
          checkpoints: [],
          outcome: this.routineOutcome,
        }),
      );

      this.setStatus('ok', 'Procedural routine saved.');
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async setPreference(): Promise<void> {
    this.setStatus('busy', 'Saving preference...');
    try {
      await firstValueFrom(
        this.http.post(`${this.baseUrl()}/self-model/preferences`, {
          key: this.prefKey,
          value: this.prefValue,
        }),
      );
      this.setStatus('ok', 'Preference saved.');
      await this.loadSelfModel();
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async loadSelfModel(): Promise<void> {
    this.setStatus('busy', 'Loading self-model...');
    try {
      const data = await firstValueFrom(this.http.get<SelfModelSnapshot>(`${this.baseUrl()}/self-model/preferences`));
      this.selfSnapshot = data;
      this.setStatus('ok', `Loaded ${data.preferences.length} preference(s).`);
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async runConsolidation(): Promise<void> {
    this.setStatus('busy', 'Running consolidation...');
    try {
      this.consolidationResult = await firstValueFrom(this.http.post(`${this.baseUrl()}/consolidation/run-once`, {}));
      this.setStatus('ok', 'Consolidation run completed.');
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async runDecay(): Promise<void> {
    this.setStatus('busy', 'Running semantic decay...');
    try {
      this.decayResult = await firstValueFrom(
        this.http.post(`${this.baseUrl()}/semantic/decay/run-once`, {
          staleDays: 30,
          decayStep: 0.05,
          minConfidence: 0.2,
        }),
      );
      this.setStatus('ok', 'Decay run completed.');
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  clearChat(): void {
    this.turns = [];
    this.thinkingExpandedByTurn = {};
    this.parsedPayloadCache = new WeakMap<ChatTurn, CachedParsedPayload>();
    this.markdownCache.clear();
    this.inlineMarkdownCache.clear();
    this.setStatus('idle', 'Chat cleared.');
  }

  setActivePage(page: ConsolePage): void {
    this.activePage = page;
  }

  isPageActive(page: ConsolePage): boolean {
    return this.activePage === page;
  }

  activePageLabel(): string {
    return this.navigation.find((x) => x.id === this.activePage)?.label ?? 'Overview';
  }

  activePageCaption(): string {
    return this.navigation.find((x) => x.id === this.activePage)?.caption ?? 'Mission control';
  }

  assistantTurnCount(): number {
    return this.turns.filter((x) => x.role === 'assistant').length;
  }

  latestAssistantTurn(): ChatTurn | null {
    for (let i = this.turns.length - 1; i >= 0; i -= 1) {
      if (this.turns[i].role === 'assistant') {
        return this.turns[i];
      }
    }

    return null;
  }

  totalToolCalls(): number {
    let total = 0;
    for (const turn of this.turns) {
      if (turn.role !== 'assistant') {
        continue;
      }

      total += turn.toolCalls?.length ?? 0;
    }

    return total;
  }

  successfulToolCalls(): number {
    let total = 0;
    for (const turn of this.turns) {
      if (turn.role !== 'assistant') {
        continue;
      }

      for (const call of turn.toolCalls ?? []) {
        if (call.succeeded) {
          total += 1;
        }
      }
    }

    return total;
  }

  toolSuccessRate(): number {
    const total = this.totalToolCalls();
    if (total === 0) {
      return 100;
    }

    return Math.round((this.successfulToolCalls() / total) * 100);
  }

  averageResponseSeconds(): number {
    const samples: number[] = [];
    for (const turn of this.turns) {
      if (turn.role !== 'assistant' || !turn.startedAtUtc || !turn.completedAtUtc) {
        continue;
      }

      const start = new Date(turn.startedAtUtc).getTime();
      const end = new Date(turn.completedAtUtc).getTime();
      if (Number.isFinite(start) && Number.isFinite(end) && end >= start) {
        samples.push((end - start) / 1000);
      }
    }

    if (samples.length === 0) {
      return 0;
    }

    const total = samples.reduce((sum, value) => sum + value, 0);
    return Number((total / samples.length).toFixed(2));
  }

  responseThroughputPerMinute(): number {
    const assistantTurns = this.turns.filter((x) => x.role === 'assistant');
    if (assistantTurns.length < 2) {
      return assistantTurns.length;
    }

    const timestamps = assistantTurns
      .map((x) => new Date(x.completedAtUtc ?? x.startedAtUtc ?? '').getTime())
      .filter((x) => Number.isFinite(x))
      .sort((a, b) => a - b);

    if (timestamps.length < 2) {
      return assistantTurns.length;
    }

    const elapsedMinutes = Math.max((timestamps[timestamps.length - 1] - timestamps[0]) / 60_000, 1);
    return Number((assistantTurns.length / elapsedMinutes).toFixed(2));
  }

  memoryLayerMetrics(): Array<{ layer: string; count: number; percent: number }> {
    const counts = new Map<string, number>();

    for (const turn of this.turns) {
      if (turn.role !== 'assistant') {
        continue;
      }

      for (const layer of turn.memoryLayers ?? []) {
        counts.set(layer, (counts.get(layer) ?? 0) + 1);
      }
    }

    const entries = Array.from(counts.entries())
      .map(([layer, count]) => ({ layer, count }))
      .sort((a, b) => b.count - a.count);

    const total = entries.reduce((sum, item) => sum + item.count, 0);
    if (total === 0) {
      return [];
    }

    return entries.map((item) => ({
      layer: item.layer,
      count: item.count,
      percent: Math.round((item.count / total) * 100),
    }));
  }

  toolOutcomeMetrics(): Array<{ label: string; count: number; percent: number }> {
    const total = this.totalToolCalls();
    const success = this.successfulToolCalls();
    const failed = Math.max(total - success, 0);
    if (total === 0) {
      return [];
    }

    return [
      { label: 'Successful', count: success, percent: Math.round((success / total) * 100) },
      { label: 'Failed', count: failed, percent: Math.round((failed / total) * 100) },
    ];
  }

  activityPolylinePoints(): string {
    const samples = this.turns
      .filter((x) => x.role === 'assistant')
      .slice(-14)
      .map((x) => Math.max(x.text.trim().length, 1));

    if (samples.length === 0) {
      return '';
    }

    const width = 430;
    const height = 130;
    const padding = 10;
    const max = Math.max(...samples);
    const denominator = Math.max(samples.length - 1, 1);

    return samples
      .map((value, index) => {
        const x = padding + (index / denominator) * (width - (padding * 2));
        const ratio = max === 0 ? 0 : value / max;
        const y = (height - padding) - (ratio * (height - (padding * 2)));
        return `${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(' ');
  }

  activityAreaPoints(): string {
    const line = this.activityPolylinePoints();
    if (!line) {
      return '';
    }

    const points = line.split(' ');
    return `10,120 ${points.join(' ')} 420,120`;
  }

  recentToolEvents(limit = 12): ToolInvocationAudit[] {
    const rows: ToolInvocationAudit[] = [];
    for (const turn of this.turns) {
      if (turn.role !== 'assistant') {
        continue;
      }

      rows.push(...(turn.toolCalls ?? []));
    }

    return rows
      .sort((a, b) => new Date(b.executedAtUtc).getTime() - new Date(a.executedAtUtc).getTime())
      .slice(0, limit);
  }

  topMemoryLayer(): string {
    const first = this.memoryLayerMetrics()[0];
    return first ? first.layer : 'none';
  }

  latestAssistantPreview(max = 160): string {
    const turn = this.latestAssistantTurn();
    if (!turn) {
      return 'No assistant response yet.';
    }

    const collapsed = turn.text.replace(/\s+/g, ' ').trim();
    if (collapsed.length <= max) {
      return collapsed || 'No assistant response yet.';
    }

    return `${collapsed.slice(0, max)}...`;
  }

  toggleThinkingForTurn(index: number): void {
    this.thinkingExpandedByTurn[index] = !this.thinkingExpandedByTurn[index];
  }

  isThinkingExpanded(index: number): boolean {
    return this.thinkingExpandedByTurn[index] === true;
  }

  assistantDisplayHtml(turn: ChatTurn): string {
    if (turn.role !== 'assistant') {
      return this.renderMarkdown(turn.text);
    }

    const parsed = this.getParsedAssistantPayload(turn);
    const primary = this.renderMarkdown(parsed.finalAnswer);
    if (primary.trim().length > 0) {
      return primary;
    }

    const withoutTags = this.stripThinkingTags(turn.text);
    const fallbackFromRaw = this.renderMarkdown(withoutTags);
    if (fallbackFromRaw.trim().length > 0) {
      return fallbackFromRaw;
    }

    const fallbackFromStages = parsed.stages.join('\n\n');
    const stageFallback = this.renderMarkdown(fallbackFromStages);
    if (stageFallback.trim().length > 0) {
      return stageFallback;
    }

    return this.renderMarkdown('_No final answer was produced for this response._');
  }

  assistantThinkingStages(turn: ChatTurn): string[] {
    if (turn.role !== 'assistant') {
      return [];
    }

    return this.getParsedAssistantPayload(turn).stages;
  }

  assistantMemoryLayers(turn: ChatTurn): string[] {
    if (turn.role !== 'assistant') {
      return [];
    }

    return turn.memoryLayers ?? [];
  }

  assistantToolCalls(turn: ChatTurn): ToolInvocationAudit[] {
    if (turn.role !== 'assistant') {
      return [];
    }

    return turn.toolCalls ?? [];
  }

  thinkingStageHtml(stage: string): string {
    const content = stage.trim();
    if (!content) {
      return '';
    }

    return this.renderInlineMarkdown(content);
  }

  private parseThinkingStagePayload(text: string): ParsedAssistantPayload {
    const normalized = text.replace(/\r\n/g, '\n').trim();

    const strict = this.parseStrictTaggedFormat(normalized);
    if (strict) {
      return strict;
    }

    const heading = this.parseHeadingFormat(normalized);
    if (heading) {
      return heading;
    }

    return { stages: [], finalAnswer: normalized };
  }

  private parseStrictTaggedFormat(text: string): ParsedAssistantPayload | null {
    const stagesBlockStart = '[THINKING_STAGES]';
    const stagesBlockEnd = '[/THINKING_STAGES]';
    const answerBlockStart = '[FINAL_ANSWER]';
    const answerBlockEnd = '[/FINAL_ANSWER]';

    const stagesMatch = /\[THINKING_STAGES\]\s*([\s\S]*?)\s*\[\/THINKING_STAGES\]/i.exec(text);
    const answerMatch = /\[FINAL_ANSWER\]\s*([\s\S]*?)\s*\[\/FINAL_ANSWER\]/i.exec(text);

    if (!stagesMatch && !answerMatch && !text.includes(stagesBlockStart) && !text.includes(answerBlockStart)) {
      return null;
    }

    let stageRaw = stagesMatch?.[1] ?? '';
    if (!stageRaw && text.includes(stagesBlockStart)) {
      const start = text.indexOf(stagesBlockStart) + stagesBlockStart.length;
      const endIndex = text.indexOf(stagesBlockEnd, start);
      stageRaw = endIndex >= 0 ? text.slice(start, endIndex) : text.slice(start);
    }

    let finalAnswer = (answerMatch?.[1] ?? '').trim();
    if (!finalAnswer && text.includes(answerBlockStart)) {
      const start = text.indexOf(answerBlockStart) + answerBlockStart.length;
      const endIndex = text.indexOf(answerBlockEnd, start);
      finalAnswer = (endIndex >= 0 ? text.slice(start, endIndex) : text.slice(start)).trim();
    }

    const stages = this.extractStageLines(stageRaw);
    const textWithoutTaggedBlocks = text
      .replace(/\[THINKING_STAGES\][\s\S]*?\[\/THINKING_STAGES\]/gi, '')
      .replace(/\[FINAL_ANSWER\]/gi, '')
      .replace(/\[\/FINAL_ANSWER\]/gi, '')
      .trim();

    const textWithoutTagMarkers = text
      .replace(/\[\/?THINKING_STAGES\]/gi, '')
      .replace(/\[\/?FINAL_ANSWER\]/gi, '')
      .trim();

    const finalAnswerForDisplay =
      finalAnswer.length > 0
        ? finalAnswer
        : textWithoutTaggedBlocks.length > 0
          ? textWithoutTaggedBlocks
          : textWithoutTagMarkers.length > 0
            ? textWithoutTagMarkers
            : text.trim();

    return {
      stages,
      finalAnswer: finalAnswerForDisplay,
    };
  }

  private parseHeadingFormat(text: string): { stages: string[]; finalAnswer: string } | null {
    const stageMatch = /(Thinking Stages|Reasoning Stages)\s*:\s*([\s\S]*?)(Final Answer\s*:|$)/i.exec(text);
    const answerMatch = /Final Answer\s*:\s*([\s\S]*)$/i.exec(text);

    if (!stageMatch && !answerMatch) {
      return null;
    }

    const stages = this.extractStageLines(stageMatch?.[2] ?? '');
    const finalAnswer = (answerMatch?.[1] ?? '').trim();

    return {
      stages,
      finalAnswer: finalAnswer.length > 0 ? finalAnswer : text.trim(),
    };
  }

  private extractStageLines(raw: string): string[] {
    const numberedNormalized = raw.replace(/\s+(?=\d+\.\s)/g, '\n');

    return numberedNormalized
      .split('\n')
      .map((line) => line.trim())
      .map((line) => line.replace(/^\d+\.\s*/, '').replace(/^[-*]\s*/, '').trim())
      .filter((line) => line.length > 0)
      .filter((line) => !line.toLowerCase().startsWith('<system>'));
  }

  private stripThinkingTags(text: string): string {
    return text
      .replace(/\[THINKING_STAGES\]/gi, '')
      .replace(/\[\/THINKING_STAGES\]/gi, '')
      .replace(/\[FINAL_ANSWER\]/gi, '')
      .replace(/\[\/FINAL_ANSWER\]/gi, '')
      .trim();
  }

  private getParsedAssistantPayload(turn: ChatTurn): ParsedAssistantPayload {
    const cached = this.parsedPayloadCache.get(turn);
    if (cached && cached.sourceText === turn.text) {
      return cached.payload;
    }

    const payload = this.parseThinkingStagePayload(turn.text);
    this.parsedPayloadCache.set(turn, { sourceText: turn.text, payload });
    return payload;
  }

  private renderMarkdown(text: string): string {
    const content = text.trim();
    if (!content) {
      return '';
    }

    const cached = this.markdownCache.get(content);
    if (cached) {
      return cached;
    }

    const rendered = marked.parse(content, {
      async: false,
      breaks: true,
      gfm: true,
    }) as string;

    this.cacheSet(this.markdownCache, content, rendered);
    return rendered;
  }

  private renderInlineMarkdown(text: string): string {
    const content = text.trim();
    if (!content) {
      return '';
    }

    const cached = this.inlineMarkdownCache.get(content);
    if (cached) {
      return cached;
    }

    const rendered = marked.parseInline(content, {
      gfm: true,
      breaks: true,
    }) as string;

    this.cacheSet(this.inlineMarkdownCache, content, rendered);
    return rendered;
  }

  private cacheSet(cache: Map<string, string>, key: string, value: string): void {
    cache.set(key, value);
    if (cache.size <= this.markdownCacheLimit) {
      return;
    }

    const oldest = cache.keys().next().value as string | undefined;
    if (oldest) {
      cache.delete(oldest);
    }
  }

  private async loadToolEvidenceForTurn(assistantIndex: number): Promise<void> {
    const turn = this.turns[assistantIndex];
    if (!turn || turn.role !== 'assistant') {
      return;
    }

    const startedAt = turn.startedAtUtc ? new Date(turn.startedAtUtc) : new Date();
    const completedAt = turn.completedAtUtc ? new Date(turn.completedAtUtc) : new Date();
    const fromUtc = new Date(startedAt.getTime() - 2000).toISOString();
    const toUtc = new Date(completedAt.getTime() + 2000).toISOString();

    try {
      const query = new URLSearchParams({
        fromUtc,
        toUtc,
        take: '50',
      });

      let rows = await firstValueFrom(
        this.http.get<ToolInvocationAudit[]>(`${this.baseUrl()}/tool-invocations?${query.toString()}`),
      );

      if (rows.length === 0) {
        // Fallback to server-clock range when client/server clocks differ.
        rows = await firstValueFrom(
          this.http.get<ToolInvocationAudit[]>(`${this.baseUrl()}/tool-invocations?take=100`),
        );
      }

      const startMs = startedAt.getTime();
      const endMs = completedAt.getTime();
      const windowed = rows.filter((row) => {
        const executedMs = new Date(row.executedAtUtc).getTime();
        return executedMs >= startMs - 60_000 && executedMs <= endMs + 60_000;
      });

      const selected = windowed.length > 0 ? windowed : rows;

      const sorted = [...selected].sort(
        (a, b) => new Date(a.executedAtUtc).getTime() - new Date(b.executedAtUtc).getTime(),
      );

      turn.toolCalls = sorted;
      turn.memoryLayers = this.inferMemoryLayersFromTools(sorted);
    } catch {
      turn.toolCalls = [];
      turn.memoryLayers = [];
    }
  }

  private inferMemoryLayersFromTools(calls: ToolInvocationAudit[]): string[] {
    const layers = new Set<string>();
    for (const call of calls) {
      const name = call.toolName.toLowerCase();
      if (name === 'store_memory' || name === 'retrieve_memory') {
        for (const layer of this.extractUnifiedToolLayers(call)) {
          layers.add(layer);
        }

        if (layers.size > 0) {
          continue;
        }
      }

      if (name.includes("working")) {
        layers.add('working');
      } else if (name.includes("episodic")) {
        layers.add('episodic');
      } else if (name.includes("semantic") || name.includes("claim")) {
        layers.add('semantic');
      } else if (name.includes("procedural") || name.includes("routine")) {
        layers.add('procedural');
      } else if (name.includes("self")) {
        layers.add('self-model');
      } else {
        layers.add('unknown');
      }
    }

    return Array.from(layers.values());
  }

  private extractUnifiedToolLayers(call: ToolInvocationAudit): string[] {
    const extracted = new Set<string>();
    const args = this.tryParseJson(call.argumentsJson);
    const result = this.tryParseJson(call.resultJson);

    const fromArgsLayer = this.normalizeLayerName(args?.layer);
    if (fromArgsLayer) {
      extracted.add(fromArgsLayer);
    }

    const fromResultLayer = this.normalizeLayerName(result?.layer);
    if (fromResultLayer) {
      extracted.add(fromResultLayer);
    }

    if (Array.isArray(result?.selectedLayers)) {
      for (const layer of result.selectedLayers) {
        const normalized = this.normalizeLayerName(layer);
        if (normalized) {
          extracted.add(normalized);
        }
      }
    }

    return Array.from(extracted.values());
  }

  private normalizeLayerName(raw: unknown): string | null {
    if (typeof raw !== 'string') {
      return null;
    }

    const normalized = raw.trim().toLowerCase();
    switch (normalized) {
      case 'working':
      case 'working-memory':
        return 'working';
      case 'episodic':
      case 'events':
        return 'episodic';
      case 'semantic':
      case 'facts':
      case 'claims':
        return 'semantic';
      case 'procedural':
      case 'routines':
        return 'procedural';
      case 'self':
      case 'self-model':
      case 'identity':
        return 'self-model';
      default:
        return null;
    }
  }

  private tryParseJson(raw: string | undefined): any | null {
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw);
    } catch {
      return null;
    }
  }

  statusClasses(): Record<string, boolean> {
    return {
      'status-idle': this.statusTone === 'idle',
      'status-busy': this.statusTone === 'busy',
      'status-ok': this.statusTone === 'ok',
      'status-error': this.statusTone === 'error',
    };
  }

  private processSseBuffer(buffer: string, assistantIndex: number): string {
    let normalized = buffer.replace(/\r\n/g, '\n');
    let split = normalized.indexOf('\n\n');

    while (split >= 0) {
      const frame = normalized.slice(0, split);
      normalized = normalized.slice(split + 2);
      this.processSseFrame(frame, assistantIndex);
      split = normalized.indexOf('\n\n');
    }

    return normalized;
  }

  private processSseFrame(frame: string, assistantIndex: number): void {
    if (!frame.trim()) {
      return;
    }

    const lines = frame.split('\n');
    let eventName = 'message';
    const dataLines: string[] = [];

    for (const line of lines) {
      if (line.startsWith('event:')) {
        eventName = line.slice(6).trim();
      } else if (line.startsWith('data:')) {
        dataLines.push(line.slice(5).trim());
      }
    }

    if (dataLines.length === 0) {
      return;
    }

    const raw = dataLines.join('\n');
    let rawChunk: RawStreamChunk;
    try {
      rawChunk = JSON.parse(raw) as RawStreamChunk;
    } catch {
      return;
    }

    if (eventName === 'error') {
      const message = typeof rawChunk.message === 'string' ? rawChunk.message : 'Stream generation failed.';
      const detail = typeof rawChunk.detail === 'string' ? rawChunk.detail : '';
      const combined = detail ? `${message} ${detail}` : message;
      this.turns[assistantIndex].text = this.turns[assistantIndex].text.trim().length > 0
        ? `${this.turns[assistantIndex].text}\n\n${combined}`
        : combined;
      this.setStatus('error', combined);
      return;
    }

    const chunk: StreamChunk = {
      sessionId: rawChunk.sessionId ?? rawChunk.SessionId ?? this.sessionId,
      delta: rawChunk.delta ?? rawChunk.Delta ?? '',
      isFinal: rawChunk.isFinal ?? rawChunk.IsFinal ?? false,
      generatedAtUtc: rawChunk.generatedAtUtc ?? rawChunk.GeneratedAtUtc ?? '',
      contextTurnCount: rawChunk.contextTurnCount ?? rawChunk.ContextTurnCount ?? 0,
    };

    this.sessionId = chunk.sessionId || this.sessionId;
    this.saveSessionId(this.sessionId);

    if (eventName === 'delta' && chunk.delta) {
      this.turns[assistantIndex].text += chunk.delta;
    }
  }

  private baseUrl(): string {
    const trimmed = this.apiBaseUrl.trim();
    if (!trimmed) {
      return '/api';
    }

    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
  }

  private setStatus(tone: StatusTone, text: string): void {
    this.statusTone = tone;
    this.statusText = text;
  }

  private toError(error: unknown): string {
    if (error instanceof Error) {
      return error.message;
    }

    return 'Unknown error';
  }

  private getOrCreateSessionId(): string {
    const key = 'cm.session_id';
    try {
      const current = localStorage.getItem(key);
      if (current && current.trim().length > 0) {
        return current;
      }

      const generated = crypto.randomUUID();
      localStorage.setItem(key, generated);
      return generated;
    } catch {
      return crypto.randomUUID();
    }
  }

  private saveSessionId(sessionId: string): void {
    try {
      localStorage.setItem('cm.session_id', sessionId);
    } catch {
      // ignore storage failures
    }
  }
}
