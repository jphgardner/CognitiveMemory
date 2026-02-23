import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { marked } from 'marked';
import { ActivatedRoute } from '@angular/router';
import { NavigationEnd, Router } from '@angular/router';
import { filter, firstValueFrom, Subscription } from 'rxjs';
import { ConsoleHeaderComponent } from './components/console-header.component';
import { ConsoleAnalyticsPageComponent } from './components/console-analytics-page.component';
import { ConsoleChatPageComponent } from './components/console-chat-page.component';
import { ConsoleDebatesPageComponent } from './components/console-debates-page.component';
import { ConsoleMemoryPageComponent } from './components/console-memory-page.component';
import { ConsoleOverviewPageComponent } from './components/console-overview-page.component';
import { ConsoleOperationsPageComponent } from './components/console-operations-page.component';
import { ConsoleSidebarComponent } from './components/console-sidebar.component';
import {
  CachedParsedPayload,
  ChatTurn,
  ConsolePage,
  DebateEventRow,
  DebateOutcomeRow,
  DebateReviewResponse,
  DebateTurnRow,
  EventingRow,
  MemoryRelationshipRow,
  MemoryView,
  PageNavItem,
  ParsedAssistantPayload,
  RawStreamChunk,
  RelationshipGraphEdge,
  RelationshipGraphNode,
  ScheduledActionRow,
  SelfModelSnapshot,
  StatusTone,
  StreamChunk,
  SubconsciousDebateRow,
  SubconsciousLifecycleRow,
  ToolInvocationAudit,
} from './models/console.models';
import { ApiUrlService } from './services/api-url.service';
import { AppContextService } from './services/app-context.service';
import { AuthService } from './services/auth.service';
import { ChatStateService } from './services/chat-state.service';
import { ClientPortalService } from './services/client-portal.service';
import { DebatesStateService } from './services/debates-state.service';
import { EventStreamService } from './services/event-stream.service';
import { MemoryStateService } from './services/memory-state.service';
import { SessionContextService } from './services/session-context.service';
import {
  ConsoleControlCenterDialogComponent,
  ConsoleControlCenterDialogResult,
} from './console-control-center-dialog.component';
import { DebateDetailDialogComponent } from './debate-detail-dialog.component';

@Component({
  selector: 'app-console-layout',
  imports: [
    CommonModule,
    FormsModule,
    ConsoleSidebarComponent,
    ConsoleHeaderComponent,
    ConsoleOverviewPageComponent,
    ConsoleAnalyticsPageComponent,
    ConsoleChatPageComponent,
    ConsoleMemoryPageComponent,
    ConsoleDebatesPageComponent,
    ConsoleOperationsPageComponent,
  ],
  templateUrl: './console-layout.component.html',
  styleUrl: './console-layout.component.css',
})
export class ConsoleLayoutComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly apiUrl = inject(ApiUrlService);
  private readonly appContext = inject(AppContextService);
  private readonly chatState = inject(ChatStateService);
  private readonly portal = inject(ClientPortalService);
  private readonly debatesApi = inject(DebatesStateService);
  private readonly eventStreams = inject(EventStreamService);
  private readonly memoryApi = inject(MemoryStateService);
  private readonly sessionContext = inject(SessionContextService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private routeEventsSub: Subscription | null = null;
  private parsedPayloadCache = new WeakMap<ChatTurn, CachedParsedPayload>();
  private readonly markdownCache = new Map<string, string>();
  private readonly inlineMarkdownCache = new Map<string, string>();
  private readonly markdownCacheLimit = 400;
  readonly navigation: PageNavItem[] = [
    { id: 'overview', label: 'Overview', caption: 'Mission control', path: '/console/overview' },
    { id: 'chat', label: 'Chat Studio', caption: 'Live streaming', path: '/console/chat' },
    { id: 'memory', label: 'Memory', caption: 'Graph + relationships', path: '/console/memory' },
    { id: 'debates', label: 'Debates', caption: 'Subconscious runtime', path: '/console/debates' },
    { id: 'analytics', label: 'Analytics', caption: 'Evidence telemetry', path: '/console/analytics' },
    { id: 'operations', label: 'Operations', caption: 'Platform runtime', path: '/console/operations' },
  ];

  apiBaseUrl = '/api';
  selectedCompanionId: string | null = this.appContext.selectedCompanionId();
  messageInput = '';
  sessionId = this.sessionContext.getOrCreate();
  turns: ChatTurn[] = [];
  selectedAssistantTurnIndex: number | null = null;
  thinkingExpandedByTurn: Record<number, boolean> = {};
  activePage: ConsolePage = 'overview';
  liveEvents: EventingRow[] = [];
  liveEventSource: EventSource | null = null;
  subconsciousDebates: SubconsciousDebateRow[] = [];
  subconsciousLifecycle: SubconsciousLifecycleRow[] = [];
  liveSubconsciousSource: EventSource | null = null;
  scheduledActions: ScheduledActionRow[] = [];
  relationships: MemoryRelationshipRow[] = [];
  liveScheduledActionsSource: EventSource | null = null;
  selectedDebateId: string | null = null;
  selectedDebateDetail: SubconsciousDebateRow | null = null;
  selectedDebateTurns: DebateTurnRow[] = [];
  selectedDebateOutcome: DebateOutcomeRow | null = null;
  selectedDebateEvents: DebateEventRow[] = [];
  selectedDebateReview: DebateReviewResponse | null = null;
  decisionInputText = '';
  decisionQueueRerun = false;
  debatesLoading = false;
  debatesActionBusy = false;
  debateEventsEndpointAvailable = true;
  debateRunTopicKey = 'manual';
  debateRunTriggerEventType = 'ManualRun';
  debateRunTriggerPayloadJson = '{}';

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
  relationshipByNodeType = 0;
  relationshipByNodeId = '';
  relationshipTypeFilter = '';
  relationshipTake = 120;
  relationshipCreateFromType = 0;
  relationshipCreateFromId = '';
  relationshipCreateToType = 0;
  relationshipCreateToId = '';
  relationshipCreateType = 'supports';
  relationshipCreateConfidence = 0.7;
  relationshipCreateStrength = 0.7;
  relationshipBackfillTake = 2000;
  relationshipExtractTake = 300;
  relationshipExtractApply = true;
  selectedGraphNodeKey: string | null = null;
  memoryView: MemoryView = 'graph';
  graphWidth = 980;
  graphHeight = 520;
  readonly relationshipNodeTypes = [
    { value: 0, label: 'SemanticClaim' },
    { value: 1, label: 'EpisodicEvent' },
    { value: 2, label: 'ProceduralRoutine' },
    { value: 3, label: 'SelfPreference' },
    { value: 4, label: 'ScheduledAction' },
    { value: 5, label: 'SubconsciousDebate' },
    { value: 6, label: 'ToolInvocation' },
  ];

  async ngOnInit(): Promise<void> {
    await this.syncSessionFromSelectedCompanion();
    this.route.paramMap.subscribe((params) => {
      const raw = (params.get('page') ?? '').toLowerCase();
      const valid = this.navigation.find((x) => x.id === raw);
      if (valid) {
        this.activePage = valid.id;
      }
    });

    this.syncActivePageFromUrl(this.router.url);
    this.routeEventsSub = this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe((event) => this.syncActivePageFromUrl(event.urlAfterRedirects));
    this.startEventStream();
    this.startSubconsciousStream();
    this.startScheduledActionsStream();
  }

  ngOnDestroy(): void {
    this.routeEventsSub?.unsubscribe();
    this.routeEventsSub = null;
    this.stopEventStream();
    this.stopSubconsciousStream();
    this.stopScheduledActionsStream();
  }

  async sendStream(): Promise<void> {
    const input = this.messageInput.trim();
    const companionId = this.selectedCompanionId?.trim() ?? '';
    if (!input || this.streaming || !companionId) {
      return;
    }

    this.streaming = true;
    this.setStatus('busy', 'Streaming response...');
    this.messageInput = '';

    const startedAtUtc = new Date().toISOString();
    this.turns = [...this.turns, { role: 'user', text: input }, { role: 'assistant', text: '', startedAtUtc }];
    const assistantIndex = this.turns.length - 1;
    this.selectedAssistantTurnIndex = assistantIndex;

    try {
      const response = await fetch(`${this.baseUrl()}/chat/stream`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'text/event-stream',
          ...(this.auth.token ? { Authorization: `Bearer ${this.auth.token}` } : {}),
        },
        body: JSON.stringify({ companionId, message: input, sessionId: this.sessionId }),
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
      this.selectedAssistantTurnIndex = assistantIndex;
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

  async loadRelationshipsBySession(): Promise<void> {
    const sessionId = this.sessionId.trim();
    const companionId = this.selectedCompanionId?.trim() ?? '';
    if (!sessionId || !companionId) {
      this.relationships = [];
      this.setStatus('error', 'Companion and session context are required.');
      return;
    }

    this.setStatus('busy', 'Loading memory relationships...');
    try {
      const rows = await this.memoryApi.loadBySession(
        this.baseUrl(),
        companionId,
        sessionId,
        this.relationshipTake,
        this.relationshipTypeFilter,
      );
      this.relationships = rows;
      this.setStatus('ok', `Loaded ${rows.length} relationship(s).`);
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async loadRelationshipsByNode(): Promise<void> {
    const sessionId = this.sessionId.trim();
    const companionId = this.selectedCompanionId?.trim() ?? '';
    const nodeId = this.relationshipByNodeId.trim();
    if (!sessionId || !nodeId || !companionId) {
      this.setStatus('error', 'Companion, session ID and node ID are required.');
      return;
    }

    this.setStatus('busy', 'Loading node relationships...');
    try {
      const rows = await this.memoryApi.loadByNode(
        this.baseUrl(),
        companionId,
        sessionId,
        this.relationshipByNodeType,
        nodeId,
        this.relationshipTake,
        this.relationshipTypeFilter,
      );
      this.relationships = rows;
      this.setStatus('ok', `Loaded ${rows.length} relationship(s) for node.`);
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async createRelationship(): Promise<void> {
    const sessionId = this.sessionId.trim();
    const companionId = this.selectedCompanionId?.trim() ?? '';
    const fromId = this.relationshipCreateFromId.trim();
    const toId = this.relationshipCreateToId.trim();
    const relationshipType = this.relationshipCreateType.trim();
    if (!sessionId || !fromId || !toId || !relationshipType || !companionId) {
      this.setStatus('error', 'Companion, session ID, From ID, To ID and relationship type are required.');
      return;
    }

    this.setStatus('busy', 'Creating memory relationship...');
    try {
      await this.memoryApi.create(this.baseUrl(), {
        companionId,
        sessionId,
        fromType: this.relationshipCreateFromType,
        fromId,
        toType: this.relationshipCreateToType,
        toId,
        relationshipType,
        confidence: this.relationshipCreateConfidence,
        strength: this.relationshipCreateStrength,
      });
      this.setStatus('ok', 'Memory relationship upserted.');
      await this.loadRelationshipsBySession();
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async retireRelationship(relationshipId: string): Promise<void> {
    if (!relationshipId) {
      return;
    }

    this.setStatus('busy', 'Retiring relationship...');
    try {
      await this.memoryApi.retire(this.baseUrl(), relationshipId);
      this.setStatus('ok', 'Relationship retired.');
      await this.loadRelationshipsBySession();
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async backfillRelationships(): Promise<void> {
    const sessionId = this.sessionId.trim();
    this.setStatus('busy', 'Running relationship backfill...');
    try {
      await this.memoryApi.backfill(this.baseUrl(), sessionId || null, this.relationshipBackfillTake);
      this.setStatus('ok', 'Relationship backfill completed.');
      await this.loadRelationshipsBySession();
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  async extractRelationshipsWithAi(): Promise<void> {
    const sessionId = this.sessionId.trim();
    if (!sessionId) {
      this.setStatus('error', 'Session ID is required.');
      return;
    }

    this.setStatus('busy', 'Running AI relationship extraction...');
    try {
      await this.memoryApi.extract(this.baseUrl(), sessionId, this.relationshipExtractTake, this.relationshipExtractApply);
      this.setStatus('ok', this.relationshipExtractApply ? 'AI extraction applied.' : 'AI extraction dry-run completed.');
      await this.loadRelationshipsBySession();
    } catch (error) {
      this.setStatus('error', this.toError(error));
    }
  }

  clearChat(): void {
    this.turns = [];
    this.selectedAssistantTurnIndex = null;
    this.liveEvents = [];
    this.subconsciousDebates = [];
    this.subconsciousLifecycle = [];
    this.scheduledActions = [];
    this.relationships = [];
    this.selectedDebateReview = null;
    this.thinkingExpandedByTurn = {};
    this.parsedPayloadCache = new WeakMap<ChatTurn, CachedParsedPayload>();
    this.markdownCache.clear();
    this.inlineMarkdownCache.clear();
    this.setStatus('idle', 'Chat cleared.');
  }

  setActivePage(page: ConsolePage): void {
    const nav = this.navigation.find((x) => x.id === page);
    if (nav) {
      void this.router.navigateByUrl(nav.path);
    }

    this.activePage = page;
    if (page === 'debates') {
      void this.refreshDebatesPage(true);
      return;
    }

    if (page === 'operations' || page === 'memory') {
      void this.loadRelationshipsBySession();
    }
  }

  openClientPortal(): void {
    void this.router.navigateByUrl('/portal');
  }

  openControlCenter(): void {
    const ref = this.dialog.open(ConsoleControlCenterDialogComponent, {
      width: '860px',
      maxWidth: '96vw',
      maxHeight: '92vh',
      panelClass: 'console-control-center-dialog',
      autoFocus: false,
      data: {
        apiBaseUrl: this.apiBaseUrl,
        sessionId: this.sessionId,
        activePage: this.activePageLabel(),
        statusText: this.statusText,
        toolSuccessRate: this.toolSuccessRate(),
        averageResponseSeconds: this.averageResponseSeconds(),
        assistantTurnCount: this.assistantTurnCount(),
        liveEventCount: this.liveEvents.length,
        debateCount: this.subconsciousDebates.length,
        scheduledCount: this.scheduledActions.length,
      },
    });

    ref.afterClosed().subscribe((result: ConsoleControlCenterDialogResult | null) => {
      if (!result) {
        return;
      }

      if (result.apiBaseUrl && result.apiBaseUrl !== this.apiBaseUrl) {
        this.onApiBaseUrlInputChanged(result.apiBaseUrl);
      }

      if (result.sessionId && result.sessionId !== this.sessionId) {
        this.onSessionIdChanged(result.sessionId);
      }

      if (result.action === 'refresh_streams') {
        this.restartStreams();
        this.setStatus('ok', 'Streams restarted from Control Center.');
      } else if (result.action === 'clear_chat') {
        this.clearChat();
      }
    });
  }

  setMemoryView(view: MemoryView): void {
    this.memoryView = view;
  }

  isMemoryView(view: MemoryView): boolean {
    return this.memoryView === view;
  }

  onApiBaseUrlChanged(): void {
    this.restartStreams();
  }

  onApiBaseUrlInputChanged(value: string): void {
    this.apiBaseUrl = value;
    this.onApiBaseUrlChanged();
  }

  onSessionIdChanged(value: string): void {
    const normalized = value.trim();
    this.sessionId = normalized;
    if (normalized.length > 0) {
      this.saveSessionId(normalized);
    }
    this.selectedDebateId = null;
    this.selectedDebateDetail = null;
    this.selectedDebateTurns = [];
    this.selectedDebateOutcome = null;
    this.selectedDebateEvents = [];
    this.selectedDebateReview = null;
    this.selectedGraphNodeKey = null;
    this.debateEventsEndpointAvailable = true;
    this.restartStreams();
    if (this.activePage === 'debates') {
      void this.refreshDebatesPage(true);
    }
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

  private syncActivePageFromUrl(url: string): void {
    const path = url.split('?')[0].split('#')[0];
    const found = this.navigation.find((item) => item.path === path);
    if (!found || found.id === this.activePage) {
      return;
    }

    this.activePage = found.id;
    if (found.id === 'debates') {
      void this.refreshDebatesPage(true);
      return;
    }

    if (found.id === 'operations' || found.id === 'memory') {
      void this.loadRelationshipsBySession();
    }
  }

  private async syncSessionFromSelectedCompanion(): Promise<void> {
    const selectedCompanionId = this.appContext.selectedCompanionId();
    if (!selectedCompanionId) {
      return;
    }

    try {
      const rows = await this.portal.listCompanions(this.baseUrl(), false);
      const selected = rows.find((x) => x.companionId === selectedCompanionId);
      if (selected?.sessionId) {
        this.selectedCompanionId = selected.companionId;
        this.sessionId = selected.sessionId;
        this.saveSessionId(selected.sessionId);
      }
    } catch {
      // keep existing session if companion lookup fails
    }
  }

  assistantTurnCount(): number {
    return this.chatState.assistantTurnCount(this.turns);
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
    return this.chatState.totalToolCalls(this.turns);
  }

  successfulToolCalls(): number {
    return this.chatState.successfulToolCalls(this.turns);
  }

  toolSuccessRate(): number {
    const total = this.totalToolCalls();
    if (total === 0) {
      return 100;
    }

    return Math.round((this.successfulToolCalls() / total) * 100);
  }

  averageResponseSeconds(): number {
    return this.chatState.averageResponseSeconds(this.turns);
  }

  responseThroughputPerMinute(): number {
    return this.chatState.responseThroughputPerMinute(this.turns);
  }

  memoryLayerMetrics(): Array<{ layer: string; count: number; percent: number }> {
    return this.chatState.memoryLayerMetrics(this.turns);
  }

  toolOutcomeMetrics(): Array<{ label: string; count: number; percent: number }> {
    return this.chatState.toolOutcomeMetrics(this.turns);
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

  selectAssistantTurn(index: number): void {
    const turn = this.turns[index];
    if (!turn || turn.role !== 'assistant') {
      return;
    }

    this.selectedAssistantTurnIndex = index;
  }

  isSelectedAssistantTurn(index: number): boolean {
    return this.selectedAssistantTurnIndex === index;
  }

  selectedAssistantTurn(): ChatTurn | null {
    if (this.selectedAssistantTurnIndex === null) {
      return this.latestAssistantTurn();
    }

    const selected = this.turns[this.selectedAssistantTurnIndex];
    if (!selected || selected.role !== 'assistant') {
      return this.latestAssistantTurn();
    }

    return selected;
  }

  selectedEventRows(): EventingRow[] {
    const rows = [...this.liveEvents].sort(
      (a, b) => new Date(b.occurredAtUtc).getTime() - new Date(a.occurredAtUtc).getTime(),
    );
    const turn = this.selectedAssistantTurn();
    if (!turn) {
      return rows.slice(0, 120);
    }

    const startedMs = turn.startedAtUtc ? new Date(turn.startedAtUtc).getTime() : Number.NaN;
    const completedMs = turn.completedAtUtc ? new Date(turn.completedAtUtc).getTime() : Number.NaN;
    if (!Number.isFinite(startedMs)) {
      return rows.slice(0, 120);
    }

    const windowStart = startedMs - 60_000;
    const windowEnd = (Number.isFinite(completedMs) ? completedMs : Date.now()) + 60_000;
    const filtered = rows.filter((row) => {
      const eventMs = new Date(row.occurredAtUtc).getTime();
      return Number.isFinite(eventMs) && eventMs >= windowStart && eventMs <= windowEnd;
    });

    return filtered.slice(0, 120);
  }

  scheduledUpcomingRows(limit = 60): ScheduledActionRow[] {
    const now = Date.now();
    return this.scheduledActions
      .filter((x) => {
        const status = (x.status ?? '').toLowerCase();
        return status === 'pending' || status === 'running';
      })
      .filter((x) => new Date(x.runAtUtc).getTime() >= now - 60_000)
      .sort((a, b) => new Date(a.runAtUtc).getTime() - new Date(b.runAtUtc).getTime())
      .slice(0, limit);
  }

  scheduledRecentResultRows(limit = 80): ScheduledActionRow[] {
    return this.scheduledActions
      .filter((x) => {
        const status = (x.status ?? '').toLowerCase();
        return status === 'completed' || status === 'failed' || status === 'canceled';
      })
      .sort((a, b) => {
        const at = new Date(a.completedAtUtc ?? a.updatedAtUtc ?? a.runAtUtc).getTime();
        const bt = new Date(b.completedAtUtc ?? b.updatedAtUtc ?? b.runAtUtc).getTime();
        return bt - at;
      })
      .slice(0, limit);
  }

  eventResultSummary(row: EventingRow): string {
    const status = (row?.status ?? '').toLowerCase();
    if (status === 'published') {
      return row.consumerCheckpointCount > 0
        ? `Delivered (${row.consumerCheckpointCount} consumers)`
        : 'Published';
    }

    if (status === 'deadletter') {
      return 'Dead-lettered';
    }

    if (status === 'failed') {
      return 'Publish failed';
    }

    if (status === 'processing') {
      return 'Processing';
    }

    return 'Pending';
  }

  eventStatusClass(row: { status?: string } | null | undefined): string {
    const status = (row?.status ?? '').toLowerCase();
    if (status === 'published') {
      return 'event-ok';
    }

    if (status === 'deadletter' || status === 'failed') {
      return 'event-fail';
    }

    return 'event-pending';
  }

  scheduledStatusClass(row: ScheduledActionRow | null | undefined): string {
    const status = (row?.status ?? '').toLowerCase();
    if (status === 'completed') {
      return 'event-ok';
    }

    if (status === 'failed' || status === 'canceled') {
      return 'event-fail';
    }

    return 'event-pending';
  }

  scheduledResultSummary(row: ScheduledActionRow): string {
    const status = (row?.status ?? '').toLowerCase();
    if (status === 'completed') {
      return 'Completed';
    }

    if (status === 'running') {
      return `Running (${row.attempts}/${row.maxAttempts})`;
    }

    if (status === 'failed') {
      return `Failed (${row.attempts}/${row.maxAttempts})`;
    }

    if (status === 'canceled') {
      return 'Canceled';
    }

    return `Pending (${row.attempts}/${row.maxAttempts})`;
  }

  scheduledInputPreview(row: ScheduledActionRow, max = 180): string {
    const raw = row?.inputJson ?? '';
    if (!raw) {
      return '{}';
    }

    try {
      const collapsed = JSON.stringify(JSON.parse(raw));
      return collapsed.length <= max ? collapsed : `${collapsed.slice(0, max)}...`;
    } catch {
      const collapsed = raw.replace(/\s+/g, ' ').trim();
      return collapsed.length <= max ? collapsed : `${collapsed.slice(0, max)}...`;
    }
  }

  relationshipNodeTypeLabel(typeValue: number | null | undefined): string {
    if (typeof typeValue !== 'number') {
      return 'Unknown';
    }

    return this.relationshipNodeTypes.find((x) => x.value === typeValue)?.label ?? String(typeValue);
  }

  relationshipStatusClass(row: MemoryRelationshipRow | null | undefined): string {
    const status = this.relationshipStatusKey(row?.status);
    if (status === 'active') {
      return 'event-ok';
    }

    if (status === 'retired') {
      return 'event-fail';
    }

    return 'event-pending';
  }

  relationshipGraphNodes(): RelationshipGraphNode[] {
    const edges = this.relationshipGraphEdges();
    if (edges.length === 0) {
      return [];
    }

    const nodeMap = new Map<string, { nodeType: number; nodeId: string; degree: number }>();
    for (const rel of this.relationships) {
      if (!this.isRelationshipVisibleInGraph(rel)) {
        continue;
      }

      const fromKey = this.relationshipNodeKey(rel.fromType, rel.fromId);
      const toKey = this.relationshipNodeKey(rel.toType, rel.toId);

      const from = nodeMap.get(fromKey) ?? { nodeType: rel.fromType, nodeId: rel.fromId, degree: 0 };
      const to = nodeMap.get(toKey) ?? { nodeType: rel.toType, nodeId: rel.toId, degree: 0 };
      from.degree += 1;
      to.degree += 1;
      nodeMap.set(fromKey, from);
      nodeMap.set(toKey, to);
    }

    const entries = Array.from(nodeMap.entries()).sort((a, b) => b[1].degree - a[1].degree);
    const cx = this.graphWidth / 2;
    const cy = this.graphHeight / 2;
    const radius = Math.max(80, Math.min(this.graphWidth, this.graphHeight) * 0.36);
    const count = Math.max(entries.length, 1);

    return entries.map(([key, value], index) => {
      const angle = (Math.PI * 2 * index) / count;
      return {
        key,
        nodeType: value.nodeType,
        nodeId: value.nodeId,
        x: cx + (radius * Math.cos(angle)),
        y: cy + (radius * Math.sin(angle)),
        degree: value.degree,
      } satisfies RelationshipGraphNode;
    });
  }

  relationshipGraphEdges(): RelationshipGraphEdge[] {
    return this.relationships
      .filter((rel) => this.isRelationshipVisibleInGraph(rel))
      .map(
        (rel) =>
          ({
            key: rel.relationshipId,
            fromKey: this.relationshipNodeKey(rel.fromType, rel.fromId),
            toKey: this.relationshipNodeKey(rel.toType, rel.toId),
            relationshipType: rel.relationshipType,
            status: this.relationshipStatusKey(rel.status),
            confidence: rel.confidence,
            strength: rel.strength,
          }) satisfies RelationshipGraphEdge,
      );
  }

  relationshipGraphRenderedEdges(): Array<RelationshipGraphEdge & { x1: number; y1: number; x2: number; y2: number }> {
    const nodes = this.relationshipGraphNodes();
    const nodeIndex = new Map(nodes.map((n) => [n.key, n] as const));
    return this.relationshipGraphEdges()
      .map((edge) => {
        const from = nodeIndex.get(edge.fromKey);
        const to = nodeIndex.get(edge.toKey);
        if (!from || !to) {
          return null;
        }

        return {
          ...edge,
          x1: from.x,
          y1: from.y,
          x2: to.x,
          y2: to.y,
        };
      })
      .filter((x): x is RelationshipGraphEdge & { x1: number; y1: number; x2: number; y2: number } => x !== null);
  }

  relationshipNodeRadius(node: RelationshipGraphNode): number {
    return Math.max(7, Math.min(16, 7 + node.degree));
  }

  relationshipNodeClass(node: RelationshipGraphNode): string {
    if (this.selectedGraphNodeKey && this.selectedGraphNodeKey === node.key) {
      return 'graph-node selected';
    }

    if (!this.selectedGraphNodeKey) {
      return 'graph-node';
    }

    const neighbor = this.relationshipGraphEdges().some(
      (edge) => (edge.fromKey === this.selectedGraphNodeKey && edge.toKey === node.key)
        || (edge.toKey === this.selectedGraphNodeKey && edge.fromKey === node.key),
    );
    return neighbor ? 'graph-node linked' : 'graph-node muted';
  }

  relationshipEdgeClass(edge: RelationshipGraphEdge): string {
    if (!this.selectedGraphNodeKey) {
      return 'graph-edge';
    }

    if (edge.fromKey === this.selectedGraphNodeKey || edge.toKey === this.selectedGraphNodeKey) {
      return 'graph-edge selected';
    }

    return 'graph-edge muted';
  }

  relationshipNodeLabel(node: RelationshipGraphNode): string {
    const type = this.relationshipNodeTypeLabel(node.nodeType);
    const id = node.nodeId.length > 24 ? `${node.nodeId.slice(0, 24)}...` : node.nodeId;
    return `${type}:${id}`;
  }

  selectGraphNode(nodeKey: string): void {
    this.selectedGraphNodeKey = this.selectedGraphNodeKey === nodeKey ? null : nodeKey;
  }

  clearGraphSelection(): void {
    this.selectedGraphNodeKey = null;
  }

  selectedGraphNode(): RelationshipGraphNode | null {
    const selected = this.selectedGraphNodeKey;
    if (!selected) {
      return null;
    }

    return this.relationshipGraphNodes().find((n) => n.key === selected) ?? null;
  }

  selectedGraphRelationships(): MemoryRelationshipRow[] {
    const selected = this.selectedGraphNodeKey;
    if (!selected) {
      return this.relationships
        .filter((rel) => this.isRelationshipVisibleInGraph(rel))
        .slice(0, 20);
    }

    return this.relationships
      .filter((rel) => this.isRelationshipVisibleInGraph(rel))
      .filter((rel) => this.relationshipNodeKey(rel.fromType, rel.fromId) === selected || this.relationshipNodeKey(rel.toType, rel.toId) === selected)
      .slice(0, 30);
  }

  graphSummaryText(): string {
    const nodes = this.relationshipGraphNodes();
    const edges = this.relationshipGraphEdges();
    const selected = this.selectedGraphNode();
    if (selected) {
      return `Selected ${this.relationshipNodeLabel(selected)} with degree ${selected.degree}.`;
    }

    return `Graph has ${nodes.length} nodes and ${edges.length} active edges.`;
  }

  private relationshipNodeKey(type: number, id: string): string {
    return `${type}:${id}`.toLowerCase();
  }

  private isRelationshipVisibleInGraph(rel: MemoryRelationshipRow): boolean {
    const status = this.relationshipStatusKey(rel.status);
    return status === 'active';
  }

  private relationshipStatusKey(status: unknown): 'active' | 'retired' | 'unknown' {
    if (typeof status === 'number') {
      if (status === 0) {
        return 'active';
      }

      if (status === 1) {
        return 'retired';
      }

      return 'unknown';
    }

    const normalized = String(status ?? '').trim().toLowerCase();
    if (normalized === 'active' || normalized === '0') {
      return 'active';
    }

    if (normalized === 'retired' || normalized === '1') {
      return 'retired';
    }

    return 'unknown';
  }

  isSubconsciousLifecycleOk(row: SubconsciousLifecycleRow | null | undefined): boolean {
    const type = row?.eventType ?? '';
    return type.endsWith('Applied') || type.endsWith('Concluded') || type.endsWith('Deferred');
  }

  isSubconsciousLifecycleFail(row: SubconsciousLifecycleRow | null | undefined): boolean {
    const type = row?.eventType ?? '';
    return type.endsWith('DebateFailed');
  }

  async refreshDebatesPage(selectLatest = false): Promise<void> {
    const sessionId = this.sessionId.trim();
    if (!sessionId) {
      this.subconsciousDebates = [];
      this.selectedDebateId = null;
      this.selectedDebateDetail = null;
      this.selectedDebateTurns = [];
      this.selectedDebateOutcome = null;
      this.selectedDebateEvents = [];
      this.selectedDebateReview = null;
      return;
    }

    this.debatesLoading = true;
    try {
      const rows = await this.debatesApi.list(this.baseUrl(), sessionId, 120);
      this.subconsciousDebates = rows;

      if (rows.length === 0) {
        this.selectedDebateId = null;
        this.selectedDebateDetail = null;
        this.selectedDebateTurns = [];
        this.selectedDebateOutcome = null;
        this.selectedDebateEvents = [];
        this.selectedDebateReview = null;
        return;
      }

      if (selectLatest || !this.selectedDebateId || !rows.some((x) => x.debateId === this.selectedDebateId)) {
        this.selectedDebateId = rows[0].debateId;
      }

      if (this.selectedDebateId) {
        await this.loadSelectedDebate(this.selectedDebateId);
      }
    } catch (error) {
      this.setStatus('error', this.toError(error));
    } finally {
      this.debatesLoading = false;
    }
  }

  async selectDebate(debateId: string): Promise<void> {
    if (!debateId || this.selectedDebateId === debateId) {
      return;
    }

    this.selectedDebateId = debateId;
    await this.loadSelectedDebate(debateId);
  }

  async openDebateDecision(debateId: string): Promise<void> {
    await this.selectDebate(debateId);
    this.activePage = 'debates';
  }

  async openDebateDialog(debateId: string): Promise<void> {
    if (!debateId) {
      return;
    }

    await this.selectDebate(debateId);
    const companionId = this.selectedCompanionId?.trim() ?? '';
    if (!companionId) {
      this.setStatus('error', 'Companion context is required to open debate details.');
      return;
    }

    const ref = this.dialog.open(DebateDetailDialogComponent, {
      width: '1180px',
      maxWidth: '98vw',
      maxHeight: '94vh',
      panelClass: 'workspace-insights-dialog',
      autoFocus: false,
      data: {
        baseUrl: this.baseUrl(),
        companionId,
        debateId,
        sessionId: this.sessionId,
      },
    });

    ref.afterClosed().subscribe(() => {
      void this.refreshDebatesPage(false);
    });
  }

  async approveSelectedDebate(): Promise<void> {
    await this.submitSelectedDebateDecision('approve');
  }

  async rejectSelectedDebate(): Promise<void> {
    await this.submitSelectedDebateDecision('reject');
  }

  async submitSelectedDebateDecision(action: 'approve' | 'reject'): Promise<void> {
    if (!this.selectedDebateId || this.debatesActionBusy) {
      return;
    }

    this.debatesActionBusy = true;
    this.setStatus('busy', `${action === 'approve' ? 'Approving' : 'Rejecting'} debate...`);
    try {
      await this.debatesApi.decide(
        this.baseUrl(),
        this.selectedDebateId,
        action,
        this.decisionInputText.trim() || null,
        this.decisionQueueRerun,
      );
      this.setStatus('ok', action === 'approve' ? 'Debate approved and concluded.' : 'Debate concluded as skipped.');
      this.decisionInputText = '';
      this.decisionQueueRerun = false;
      await this.refreshDebatesPage(false);
    } catch (error) {
      this.setStatus('error', this.toError(error));
    } finally {
      this.debatesActionBusy = false;
    }
  }

  async rerunSelectedDebate(): Promise<void> {
    const detail = this.selectedDebateDetail;
    if (!detail || this.debatesActionBusy) {
      return;
    }

    this.debatesActionBusy = true;
    this.setStatus('busy', 'Queueing debate rerun...');
    try {
      await this.debatesApi.rerun(this.baseUrl(), {
        sessionId: detail.sessionId ?? this.sessionId,
        topicKey: detail.topicKey,
        triggerEventType: detail.triggerEventType,
        triggerPayloadJson: detail.triggerPayloadJson ?? '{}',
      });
      this.setStatus('ok', 'Debate rerun queued.');
      await this.refreshDebatesPage(true);
    } catch (error) {
      this.setStatus('error', this.toError(error));
    } finally {
      this.debatesActionBusy = false;
    }
  }

  async runManualDebate(): Promise<void> {
    if (this.debatesActionBusy) {
      return;
    }

    const sessionId = this.sessionId.trim();
    if (!sessionId) {
      this.setStatus('error', 'Session ID is required.');
      return;
    }

    this.debatesActionBusy = true;
    this.setStatus('busy', 'Queueing manual debate...');
    try {
      await this.debatesApi.rerun(this.baseUrl(), {
        sessionId,
        topicKey: this.debateRunTopicKey.trim() || 'manual',
        triggerEventType: this.debateRunTriggerEventType.trim() || 'ManualRun',
        triggerPayloadJson: this.debateRunTriggerPayloadJson.trim() || '{}',
      });
      this.setStatus('ok', 'Manual debate queued.');
      await this.refreshDebatesPage(true);
    } catch (error) {
      this.setStatus('error', this.toError(error));
    } finally {
      this.debatesActionBusy = false;
    }
  }

  selectedDebateSummary(): string {
    const row = this.selectedDebateDetail;
    if (!row) {
      return 'No debate selected.';
    }

    return `${row.topicKey} · ${row.state} · ${row.triggerEventType}`;
  }

  debateCardClass(row: SubconsciousDebateRow | null | undefined): string {
    const state = (row?.state ?? '').toLowerCase();
    if (state === 'completed') {
      return 'event-ok';
    }

    if (state === 'awaitinguser') {
      return 'event-pending';
    }

    return 'event-pending';
  }

  debateResolutionLabel(row: SubconsciousDebateRow | null | undefined, outcome?: DebateOutcomeRow | null): string {
    const state = (row?.state ?? '').toLowerCase();
    const apply = (outcome?.applyStatus ?? '').toLowerCase();

    if (state === 'awaitinguser') {
      return 'Awaiting decision';
    }

    if (state === 'completed' && apply === 'deferred') {
      return 'Concluded · Deferred';
    }

    if (state === 'completed' && apply === 'applied') {
      return 'Concluded · Applied';
    }

    if (state === 'completed' && apply === 'skipped') {
      return 'Concluded · Skipped';
    }

    if (state === 'running') {
      return 'Running';
    }

    if (state === 'queued') {
      return 'Queued';
    }

    return row?.state ?? 'Unknown';
  }

  debateOutcomePrettyJson(): string {
    const json = this.selectedDebateOutcome?.outcomeJson;
    if (!json) {
      return 'No outcome recorded yet.';
    }

    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }

  debateReviewPrettyJson(): string {
    if (!this.selectedDebateReview) {
      return 'No approval review available yet.';
    }

    return JSON.stringify(this.selectedDebateReview, null, 2);
  }

  debateTurnPreview(turn: DebateTurnRow, max = 180): string {
    const raw = turn?.message ?? '';
    const collapsed = raw.replace(/\s+/g, ' ').trim();
    if (!collapsed) {
      return 'No message content.';
    }

    return collapsed.length <= max ? collapsed : `${collapsed.slice(0, max)}...`;
  }

  private async loadSelectedDebate(debateId: string): Promise<void> {
    const detail = await this.debatesApi.detail(this.baseUrl(), debateId);
    const companionId = this.selectedCompanionId ?? '';
    const [turns, outcome, events, review] = await Promise.all([
      this.debatesApi.turns(this.baseUrl(), debateId),
      this.debatesApi.outcome(this.baseUrl(), debateId),
      this.loadDebateEventsWithFallback(debateId, companionId),
      this.debatesApi.review(this.baseUrl(), debateId),
    ]);

    this.selectedDebateDetail = detail;
    this.selectedDebateTurns = turns;
    this.selectedDebateOutcome = outcome;
    this.selectedDebateEvents = events;
    this.selectedDebateReview = review;
  }

  private async loadDebateEventsWithFallback(debateId: string, companionId: string): Promise<DebateEventRow[]> {
    const result = await this.debatesApi.events(this.baseUrl(), debateId, companionId, this.debateEventsEndpointAvailable);
    this.debateEventsEndpointAvailable = result.endpointAvailable;
    return result.rows;
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

    const nextSessionId = chunk.sessionId || this.sessionId;
    if (nextSessionId !== this.sessionId) {
      this.sessionId = nextSessionId;
      this.saveSessionId(this.sessionId);
      this.restartStreams();
    } else {
      this.sessionId = nextSessionId;
      this.saveSessionId(this.sessionId);
    }

    if (eventName === 'delta' && chunk.delta) {
      this.turns[assistantIndex].text += chunk.delta;
    }
  }

  private startEventStream(): void {
    if (this.liveEventSource !== null) {
      return;
    }

    const companionId = this.selectedCompanionId?.trim() ?? '';
    if (!companionId) {
      this.liveEvents = [];
      return;
    }

    const source = this.eventStreams.openEventingStream(
      this.baseUrl(),
      companionId,
      (rows) => {
        this.liveEvents = rows;
        this.cdr.detectChanges();
      },
      () => this.cdr.detectChanges(),
    );

    this.liveEventSource = source;
  }

  private stopEventStream(): void {
    if (this.liveEventSource === null) {
      return;
    }

    this.liveEventSource.close();
    this.liveEventSource = null;
  }

  private restartEventStream(): void {
    this.stopEventStream();
    this.startEventStream();
  }

  private startSubconsciousStream(): void {
    if (this.liveSubconsciousSource !== null) {
      return;
    }

    const sessionId = this.sessionId.trim();
    if (!sessionId) {
      this.subconsciousDebates = [];
      this.subconsciousLifecycle = [];
      return;
    }

    const source = this.eventStreams.openSubconsciousStream(
      this.baseUrl(),
      sessionId,
      (rows) => {
        this.subconsciousDebates = rows;
        if (this.activePage === 'debates') {
          const selectedId = this.selectedDebateId;
          if (!selectedId && this.subconsciousDebates.length > 0) {
            this.selectedDebateId = this.subconsciousDebates[0].debateId;
            void this.loadSelectedDebate(this.selectedDebateId);
          } else if (selectedId) {
            const row = this.subconsciousDebates.find((x) => x.debateId === selectedId);
            if (!row) {
              this.selectedDebateId = this.subconsciousDebates[0]?.debateId ?? null;
              if (this.selectedDebateId) {
                void this.loadSelectedDebate(this.selectedDebateId);
              }
            } else if (this.selectedDebateDetail?.updatedAtUtc !== row.updatedAtUtc) {
              void this.loadSelectedDebate(selectedId);
            }
          }
        }
        this.cdr.detectChanges();
      },
      (row) => {
        this.subconsciousLifecycle = [row, ...this.subconsciousLifecycle]
          .filter((x, index, arr) => arr.findIndex((y) => y.eventId === x.eventId) === index)
          .slice(0, 120);
        this.cdr.detectChanges();
      },
      () => this.cdr.detectChanges(),
    );

    this.liveSubconsciousSource = source;
  }

  private stopSubconsciousStream(): void {
    if (this.liveSubconsciousSource === null) {
      return;
    }

    this.liveSubconsciousSource.close();
    this.liveSubconsciousSource = null;
  }

  private restartSubconsciousStream(): void {
    this.stopSubconsciousStream();
    this.subconsciousLifecycle = [];
    this.startSubconsciousStream();
  }

  private restartStreams(): void {
    this.restartEventStream();
    this.restartSubconsciousStream();
    this.restartScheduledActionsStream();
  }

  private async refreshScheduledActionsSnapshot(): Promise<void> {
    const companionId = this.selectedCompanionId?.trim() ?? '';
    if (!companionId) {
      this.scheduledActions = [];
      return;
    }

    try {
      const query = new URLSearchParams({
        companionId,
        take: '250',
      });
      const rows = await firstValueFrom(
        this.http.get<ScheduledActionRow[]>(`${this.baseUrl()}/scheduled-actions?${query.toString()}`),
      );
      this.scheduledActions = rows;
      this.cdr.detectChanges();
    } catch {
      // Keep prior rows when endpoint/network is transiently unavailable.
    }
  }

  private startScheduledActionsStream(): void {
    if (this.liveScheduledActionsSource !== null) {
      return;
    }

    const companionId = this.selectedCompanionId?.trim() ?? '';
    if (!companionId) {
      this.scheduledActions = [];
      return;
    }

    const source = this.eventStreams.openScheduledActionsStream(
      this.baseUrl(),
      companionId,
      (rows) => {
        this.scheduledActions = rows;
        this.cdr.detectChanges();
      },
      () => {
        void this.refreshScheduledActionsSnapshot();
        this.cdr.detectChanges();
      },
    );

    this.liveScheduledActionsSource = source;
  }

  private stopScheduledActionsStream(): void {
    if (this.liveScheduledActionsSource === null) {
      return;
    }

    this.liveScheduledActionsSource.close();
    this.liveScheduledActionsSource = null;
  }

  private restartScheduledActionsStream(): void {
    this.stopScheduledActionsStream();
    this.startScheduledActionsStream();
  }

  private baseUrl(): string {
    return this.apiUrl.normalize(this.apiBaseUrl);
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

  private saveSessionId(sessionId: string): void {
    this.sessionContext.save(sessionId);
  }
}
