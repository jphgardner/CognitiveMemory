import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { marked } from 'marked';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AppContextService } from './services/app-context.service';
import { ClientPortalService, CompanionProfile } from './services/client-portal.service';
import { EventStreamService } from './services/event-stream.service';
import { AuthService } from './services/auth.service';
import { EventingRow, MemoryView, SubconsciousDebateRow, SubconsciousLifecycleRow, ToolInvocationAudit } from './models/console.models';
import { SessionContextService } from './services/session-context.service';
import { WorkspaceInsightsDialogComponent } from './workspace-insights-dialog.component';
import { DebateDetailDialogComponent } from './debate-detail-dialog.component';
import { WorkspaceMemoryExplorerComponent } from './workspace-memory-explorer.component';

interface WorkspaceSummary {
  companionId: string;
  name: string;
  tone: string;
  purpose: string;
  modelHint: string;
  sessionId: string;
  originStory: string;
  birthDateUtc?: string | null;
  semanticClaimCount: number;
  episodicEventCount: number;
  relationshipCount: number;
  debateCount: number;
  lastActivityUtc?: string | null;
}

interface WorkspaceClaim {
  claimId: string;
  predicate: string;
  value: string;
  confidence: number;
  status: string;
  updatedAtUtc: string;
}

interface WorkspaceRelationship {
  relationshipId: string;
  fromType: string;
  fromId: string;
  toType: string;
  toId: string;
  relationshipType: string;
  confidence: number;
  strength: number;
  status: string;
  updatedAtUtc: string;
}

interface WorkspacePacket {
  claims: WorkspaceClaim[];
  contradictions: Array<{ contradictionId: string; type: string; severity: string; status: string; detectedAtUtc: string }>;
  relationships: WorkspaceRelationship[];
}

interface WorkspaceMetrics {
  totalToolCalls: number;
  successfulToolCalls: number;
  toolSuccessRate: number;
  averagePublishLatencySeconds: number;
  layerDistribution: Array<{ layer: string; count: number; percent: number }>;
}

interface WorkspaceTurn {
  role: 'user' | 'assistant';
  text: string;
  createdAtUtc: string;
  toolCalls?: ToolInvocationAudit[];
}

interface WorkspaceRawStreamChunk {
  sessionId?: string;
  SessionId?: string;
  delta?: string;
  Delta?: string;
  isFinal?: boolean;
  IsFinal?: boolean;
  message?: string;
  detail?: string;
}

interface MemoryTimelineLink {
  relationshipType: string;
  direction: 'incoming' | 'outgoing';
  peerLabel: string;
  peerValue: string;
  updatedAtUtc: string;
}

type MemoryTimelineKind = 'semantic' | 'episodic' | 'self' | 'procedural' | 'scheduled' | 'debate' | 'tool' | 'unknown';

interface MemoryTimelineItem {
  key: string;
  nodeType: number;
  nodeId: string;
  kind: MemoryTimelineKind;
  title: string;
  value: string;
  meta: string;
  updatedAtUtc: string;
  relationships: MemoryTimelineLink[];
}

interface MemoryTimelineGroup {
  dateKey: string;
  dateLabel: string;
  items: MemoryTimelineItem[];
}

interface MemoryTimelinePageResponse {
  page: number;
  pageSize: number;
  total: number;
  items: MemoryTimelineItem[];
}

interface EvidenceGroup {
  eventType: string;
  events: EventingRow[];
  total: number;
  latestAtUtc: string;
}

@Component({
  selector: 'app-workspace',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatTabsModule, MatIconModule, WorkspaceMemoryExplorerComponent],
  templateUrl: './workspace.component.html',
  styles: [`
    .timeline-filters {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 0.7rem;
      align-items: end;
    }
    .timeline-filters .search-wide {
      grid-column: span 1;
    }
    .timeline-root {
      display: grid;
      gap: 1rem;
    }
    .timeline-day {
      border: 1px solid rgba(148, 163, 184, 0.25);
      border-radius: 0.9rem;
      padding: 0.75rem;
      background: rgba(2, 6, 23, 0.2);
    }
    .timeline-day-head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 0.6rem;
      margin-bottom: 0.5rem;
    }
    .timeline-day-head h4 {
      margin: 0;
      font-size: 0.96rem;
      letter-spacing: 0.01em;
    }
    .timeline-rail {
      position: relative;
      display: grid;
      gap: 0.8rem;
      padding-left: 1.35rem;
    }
    .timeline-rail::before {
      content: '';
      position: absolute;
      left: 0.45rem;
      top: 0.2rem;
      bottom: 0.2rem;
      width: 2px;
      background: linear-gradient(180deg, rgba(56, 189, 248, 0.8), rgba(148, 163, 184, 0.35));
      border-radius: 999px;
    }
    .timeline-node {
      position: relative;
    }
    .timeline-dot {
      position: absolute;
      left: -1.05rem;
      top: 0.55rem;
      width: 0.72rem;
      height: 0.72rem;
      border-radius: 999px;
      background: rgba(34, 211, 238, 0.9);
      box-shadow: 0 0 0 3px rgba(34, 211, 238, 0.18);
    }
    .timeline-card {
      border: 1px solid rgba(148, 163, 184, 0.22);
      border-radius: 0.7rem;
      padding: 0.7rem;
      background: rgba(15, 23, 42, 0.28);
    }
    .timeline-links {
      display: grid;
      gap: 0.45rem;
    }
    .timeline-paging {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 0.65rem;
      flex-wrap: wrap;
    }
    .timeline-paging .actions {
      display: flex;
      align-items: center;
      gap: 0.45rem;
      flex-wrap: wrap;
    }
    .evidence-controls {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 190px;
      gap: 0.55rem;
      align-items: end;
    }
    .evidence-group-list {
      display: grid;
      gap: 0.6rem;
      max-height: 70vh;
      overflow: auto;
      padding-right: 0.2rem;
    }
    .evidence-group {
      border: 1px solid rgba(148, 163, 184, 0.25);
      border-radius: 0.85rem;
      background: rgba(15, 23, 42, 0.26);
      overflow: hidden;
    }
    .evidence-group-head {
      width: 100%;
      border: 0;
      background: transparent;
      color: inherit;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      padding: 0.6rem 0.7rem;
      cursor: pointer;
    }
    .evidence-group-head:hover {
      background: rgba(148, 163, 184, 0.08);
    }
    .evidence-group-main {
      display: flex;
      align-items: center;
      gap: 0.45rem;
      min-width: 0;
    }
    .evidence-group-title {
      font-weight: 700;
      font-size: 0.88rem;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      max-width: 15rem;
    }
    .evidence-count {
      border-radius: 999px;
      border: 1px solid rgba(148, 163, 184, 0.28);
      padding: 0.1rem 0.45rem;
      font-size: 0.72rem;
      color: rgba(226, 232, 240, 0.94);
      background: rgba(15, 23, 42, 0.35);
    }
    .evidence-group-time {
      font-size: 0.72rem;
      color: rgba(148, 163, 184, 0.95);
    }
    .evidence-items {
      display: grid;
      gap: 0.4rem;
      padding: 0 0.65rem 0.65rem 0.65rem;
    }
    .evidence-row {
      border: 1px solid rgba(148, 163, 184, 0.2);
      border-radius: 0.68rem;
      padding: 0.5rem;
      background: rgba(15, 23, 42, 0.32);
    }
    .evidence-row-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.45rem;
    }
    .evidence-row-meta {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      flex-wrap: wrap;
    }
    .evidence-status {
      border-radius: 999px;
      padding: 0.1rem 0.4rem;
      font-size: 0.68rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      background: rgba(148, 163, 184, 0.2);
      border: 1px solid rgba(148, 163, 184, 0.35);
    }
    .evidence-status.ok {
      color: #86efac;
      border-color: rgba(134, 239, 172, 0.4);
      background: rgba(22, 101, 52, 0.25);
    }
    .evidence-status.error {
      color: #fca5a5;
      border-color: rgba(252, 165, 165, 0.45);
      background: rgba(127, 29, 29, 0.24);
    }
    .evidence-status.pending {
      color: #fde68a;
      border-color: rgba(253, 230, 138, 0.45);
      background: rgba(113, 63, 18, 0.26);
    }
    .evidence-row-time {
      font-size: 0.72rem;
      color: rgba(148, 163, 184, 0.95);
    }
    .evidence-preview {
      margin: 0.38rem 0 0;
      color: rgba(203, 213, 225, 0.92);
      font-size: 0.8rem;
      line-height: 1.35;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
      word-break: break-word;
    }
    .evidence-detail {
      margin-top: 0.42rem;
      border: 1px solid rgba(148, 163, 184, 0.25);
      border-radius: 0.56rem;
      background: rgba(2, 6, 23, 0.45);
      padding: 0.45rem;
      font-family: 'IBM Plex Mono', ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
      font-size: 0.72rem;
      line-height: 1.35;
      max-height: 9.5rem;
      overflow: auto;
      white-space: pre-wrap;
      word-break: break-word;
    }
    @media (max-width: 980px) {
      .timeline-filters {
        grid-template-columns: 1fr 1fr;
      }
      .timeline-filters .search-wide {
        grid-column: span 2;
      }
      .evidence-controls {
        grid-template-columns: 1fr;
      }
    }
    @media (max-width: 640px) {
      .timeline-filters {
        grid-template-columns: 1fr;
      }
      .timeline-filters .search-wide {
        grid-column: span 1;
      }
      .timeline-rail {
        padding-left: 1.15rem;
      }
      .timeline-dot {
        left: -0.9rem;
      }
    }
  `],
})
export class WorkspaceComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly portal = inject(ClientPortalService);
  private readonly context = inject(AppContextService);
  private readonly http = inject(HttpClient);
  private readonly streams = inject(EventStreamService);
  private readonly sessionContext = inject(SessionContextService);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);

  readonly companion = signal<CompanionProfile | null>(null);
  readonly summary = signal<WorkspaceSummary | null>(null);
  readonly packet = signal<WorkspacePacket | null>(null);
  readonly metrics = signal<WorkspaceMetrics | null>(null);
  readonly loading = signal(false);
  readonly messageInput = signal('');
  readonly streaming = signal(false);
  readonly turns = signal<WorkspaceTurn[]>([]);
  readonly events = signal<EventingRow[]>([]);
  readonly evidenceSearch = signal('');
  readonly evidenceTypeFilter = signal('all');
  readonly evidenceExpandedGroups = signal<Record<string, boolean>>({});
  readonly selectedEvidenceEventId = signal<string | null>(null);
  readonly evidencePerGroup = signal(6);
  readonly debates = signal<SubconsciousDebateRow[]>([]);
  readonly lifecycle = signal<SubconsciousLifecycleRow[]>([]);
  readonly memoryExplorerView = signal<MemoryView>('graph');
  readonly memoryExplorerRefreshKey = signal(0);
  readonly memoryTabMode = signal<'timeline' | 'graph'>('timeline');
  readonly memoryTimelineLoading = signal(false);
  readonly memoryTimelineItems = signal<MemoryTimelineItem[]>([]);
  readonly memoryTimelineTotal = signal(0);
  readonly memoryTimelineClaims = signal<Array<{ claimId: string; predicate: string; value: string; confidence: number; status: string; updatedAtUtc: string }>>([]);
  readonly timelineFilterKind = signal<'all' | MemoryTimelineKind>('all');
  readonly timelineFilterLinked = signal<'all' | 'linked' | 'unlinked'>('all');
  readonly timelineFilterQuery = signal('');
  readonly timelinePage = signal(1);
  readonly timelinePageSize = signal(20);
  readonly expandedTimelineRelations = signal<Record<string, boolean>>({});
  readonly filteredTimelineItems = computed(() => this.memoryTimelineItems());
  readonly groupedTimelineItems = computed(() => {
    const groups = new Map<string, MemoryTimelineItem[]>();
    for (const item of this.filteredTimelineItems()) {
      const date = new Date(item.updatedAtUtc);
      const key = Number.isNaN(date.getTime()) ? 'unknown' : date.toISOString().slice(0, 10);
      const bucket = groups.get(key) ?? [];
      bucket.push(item);
      groups.set(key, bucket);
    }

    return Array.from(groups.entries())
      .sort((a, b) => b[0].localeCompare(a[0]))
      .map(([dateKey, items]): MemoryTimelineGroup => ({
        dateKey,
        dateLabel: dateKey === 'unknown' ? 'Unknown date' : new Date(`${dateKey}T00:00:00Z`).toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' }),
        items: items.sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime()),
      }));
  });
  readonly timelineTotalPages = computed(() => {
    const total = this.memoryTimelineTotal();
    const size = Math.max(1, this.timelinePageSize());
    return Math.max(1, Math.ceil(total / size));
  });
  readonly timelinePageStart = computed(() => {
    const total = this.memoryTimelineTotal();
    if (total === 0) {
      return 0;
    }
    return Math.min(total, (this.timelinePage() - 1) * this.timelinePageSize() + 1);
  });
  readonly timelinePageEnd = computed(() => {
    const total = this.memoryTimelineTotal();
    if (total === 0) {
      return 0;
    }
    return Math.min(total, this.timelinePageStart() + this.memoryTimelineItems().length - 1);
  });
  readonly canSend = computed(() => this.messageInput().trim().length > 0 && !this.streaming());
  readonly evidenceTypes = computed(() => {
    const set = new Set<string>();
    for (const row of this.events()) {
      set.add((row.eventType ?? 'Unknown').trim() || 'Unknown');
    }
    return Array.from(set.values()).sort((a, b) => a.localeCompare(b));
  });
  readonly filteredEvidenceEvents = computed(() => {
    const typeFilter = this.evidenceTypeFilter().trim().toLowerCase();
    const query = this.evidenceSearch().trim().toLowerCase();
    return this.events().filter((row) => {
      const type = (row.eventType ?? '').trim();
      if (typeFilter && typeFilter !== 'all' && type.toLowerCase() !== typeFilter) {
        return false;
      }

      if (!query) {
        return true;
      }

      const payload = (row.payloadPreview ?? '').toLowerCase();
      const status = (row.status ?? '').toLowerCase();
      const aggregate = `${row.aggregateType ?? ''} ${row.aggregateId ?? ''}`.toLowerCase();
      return type.toLowerCase().includes(query) || payload.includes(query) || status.includes(query) || aggregate.includes(query);
    });
  });
  readonly groupedEvidenceEvents = computed(() => {
    const groups = new Map<string, EventingRow[]>();
    for (const row of this.filteredEvidenceEvents()) {
      const type = (row.eventType ?? 'Unknown').trim() || 'Unknown';
      const bucket = groups.get(type) ?? [];
      bucket.push(row);
      groups.set(type, bucket);
    }

    const result: EvidenceGroup[] = [];
    for (const [eventType, rows] of groups.entries()) {
      rows.sort((a, b) => new Date(b.occurredAtUtc).getTime() - new Date(a.occurredAtUtc).getTime());
      result.push({
        eventType,
        events: rows,
        total: rows.length,
        latestAtUtc: rows[0]?.occurredAtUtc ?? '',
      });
    }

    return result.sort((a, b) => new Date(b.latestAtUtc).getTime() - new Date(a.latestAtUtc).getTime());
  });

  private eventSource: EventSource | null = null;
  private subconsciousSource: EventSource | null = null;
  private readonly markdownCache = new Map<string, string>();
  private readonly markdownCacheLimit = 400;
  private lastEventSnapshotSignature = '';

  async ngOnInit(): Promise<void> {
    this.route.paramMap.subscribe(() => {
      void this.resolveCompanion();
    });
    this.route.queryParamMap.subscribe((params) => {
      this.applyMemoryViewFromQuery(params.get('memoryView'));
      this.applyTimelineQueryFromParams(params);
    });
  }

  ngOnDestroy(): void {
    this.eventSource?.close();
    this.subconsciousSource?.close();
    this.eventSource = null;
    this.subconsciousSource = null;
  }

  async refresh(): Promise<void> {
    const companion = this.companion();
    if (!companion) {
      return;
    }

    this.loading.set(true);
    try {
      const base = this.baseUrl();
      const id = encodeURIComponent(companion.companionId);
      const [summary, packet, metrics, debates] = await Promise.all([
        firstValueFrom(this.http.get<WorkspaceSummary>(`${base}/workspace/companion/${id}/summary`)),
        firstValueFrom(this.http.get<WorkspacePacket>(`${base}/workspace/companion/${id}/memory-packet?take=24`)),
        firstValueFrom(this.http.get<WorkspaceMetrics>(`${base}/workspace/companion/${id}/metrics?take=200`)),
        firstValueFrom(this.http.get<SubconsciousDebateRow[]>(`${base}/subconscious/debates/${encodeURIComponent(companion.sessionId)}?take=40`)),
      ]);
      this.summary.set(summary);
      this.packet.set(packet);
      this.metrics.set(metrics);
      this.debates.set(debates);
      this.memoryTimelineClaims.set(
        (packet?.claims ?? []).map((row) => ({
          claimId: row.claimId,
          predicate: row.predicate,
          value: row.value,
          confidence: Number(row.confidence) || 0,
          status: row.status,
          updatedAtUtc: row.updatedAtUtc,
        })),
      );
      await this.refreshMemoryTimeline(companion);
      this.bumpMemoryExplorerRefresh();
      this.startStreams();
    } finally {
      this.loading.set(false);
    }
  }

  async sendChat(): Promise<void> {
    const selected = this.companion();
    const input = this.messageInput().trim();
    if (!selected || !input || this.streaming()) {
      return;
    }

    this.messageInput.set('');
    this.streaming.set(true);
    const now = new Date().toISOString();
    const assistantIndex = this.turns().length + 1;
    this.turns.update((rows) => [...rows, { role: 'user', text: input, createdAtUtc: now }, { role: 'assistant', text: '', createdAtUtc: now }]);

    try {
      const response = await fetch(`${this.baseUrl()}/chat/stream`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          Accept: 'text/event-stream',
          ...(this.auth.token ? { Authorization: `Bearer ${this.auth.token}` } : {}),
        },
        body: JSON.stringify({ companionId: selected.companionId, sessionId: selected.sessionId, message: input }),
      });
      if (!response.ok || !response.body) {
        throw new Error(`Chat failed (${response.status}).`);
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
      }

      buffer += decoder.decode();
      if (buffer.trim()) {
        this.processSseFrame(buffer, assistantIndex);
      }

      const assistant = this.turns()[assistantIndex];
      if (assistant && !assistant.text.trim()) {
        this.updateAssistantTurn(assistantIndex, (turn) => ({ ...turn, text: '[No response returned]' }));
      }

      try {
        await this.loadToolEvidenceForTurn(assistantIndex);
      } catch {
        // Tool evidence is non-critical for chat rendering.
      }

      try {
        await this.refresh();
      } catch {
        // Keep streamed answer visible even if background refresh fails.
      }
    } catch (error) {
      const message = this.toError(error);
      this.updateAssistantTurn(assistantIndex, (turn) => ({
        ...turn,
        text: turn.text.trim().length > 0 ? `${turn.text}\n\n[Request issue: ${message}]` : `Request failed: ${message}`,
      }));
    } finally {
      this.streaming.set(false);
    }
  }

  async runDebate(topic = 'context-refinement'): Promise<void> {
    const companion = this.companion();
    if (!companion) {
      return;
    }

    await firstValueFrom(
      this.http.post(`${this.baseUrl()}/subconscious/debates/run-once`, {
        sessionId: companion.sessionId,
        topicKey: topic,
        triggerEventType: 'ManualRun',
        triggerPayloadJson: '{}',
      }),
    );
    await this.refresh();
  }

  async createQuickClaim(): Promise<void> {
    const companion = this.companion();
    if (!companion) {
      return;
    }

    const text = this.messageInput().trim();
    if (!text) {
      return;
    }

    await firstValueFrom(
      this.http.post(`${this.baseUrl()}/semantic/claims`, {
        subject: `session:${companion.sessionId}`,
        predicate: 'workspace.note',
        value: text,
        confidence: 0.7,
        scope: 'session',
      }),
    );
    this.messageInput.set('');
    await this.refresh();
  }

  openConsole(page: 'chat' | 'memory' | 'debates' | 'analytics' | 'operations'): void {
    const companion = this.companion();
    if (companion) {
      this.context.setSelectedCompanionId(companion.companionId);
      this.sessionContext.save(companion.sessionId);
    }
    void this.router.navigate(['/console', page]);
  }

  openInsightsDialog(): void {
    const companion = this.companion();
    if (!companion) {
      return;
    }

    const packet = this.packet();
    this.dialog.open(WorkspaceInsightsDialogComponent, {
      width: '1100px',
      maxWidth: '97vw',
      maxHeight: '94vh',
      panelClass: 'workspace-insights-dialog',
      autoFocus: false,
      data: {
        companionName: companion.name,
        events: this.events(),
        claims: (packet?.claims ?? []).map((claim) => ({
          predicate: claim.predicate,
          value: claim.value,
          confidence: claim.confidence,
        })),
        relationships: (packet?.relationships ?? []).map((rel) => ({
          relationshipType: rel.relationshipType,
          fromType: rel.fromType,
          fromId: rel.fromId,
          toType: rel.toType,
          toId: rel.toId,
        })),
        debates: this.debates(),
        metrics: this.metrics(),
      },
    });
  }

  openDebate(debateId: string): void {
    const companion = this.companion();
    if (!companion || !debateId) {
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
        companionId: companion.companionId,
        debateId,
        sessionId: companion.sessionId,
      },
    });

    ref.afterClosed().subscribe(() => {
      void this.refresh();
    });
  }

  private async resolveCompanion(): Promise<void> {
    const routeId = this.route.snapshot.paramMap.get('companionId');
    const selected = routeId ?? this.context.selectedCompanionId();
    if (!selected) {
      this.companion.set(null);
      return;
    }

    const rows = await this.portal.listCompanions(this.baseUrl(), false);
    let resolved = rows.find((x) => x.companionId === selected) ?? null;
    if (!resolved && rows.length > 0) {
      resolved = rows[0];
      void this.router.navigate(['/workspace', resolved.companionId]);
    }

    this.companion.set(resolved);
    if (resolved) {
      this.context.setSelectedCompanionId(resolved.companionId);
      this.sessionContext.save(resolved.sessionId);
      await this.refresh();
    }
  }

  private startStreams(): void {
    this.eventSource?.close();
    this.subconsciousSource?.close();
    const companion = this.companion();
    if (!companion) {
      return;
    }

    this.eventSource = this.streams.openEventingStream(
      this.baseUrl(),
      companion.companionId,
      (rows) => {
        this.events.set(rows);
        const signature = rows.map((x) => x.eventId).slice(0, 16).join('|');
        if (signature !== this.lastEventSnapshotSignature) {
          this.lastEventSnapshotSignature = signature;
          if (this.hasMemoryImpactEvents(rows)) {
            this.bumpMemoryExplorerRefresh();
          }
        }
      },
      () => undefined,
    );

    this.subconsciousSource = this.streams.openSubconsciousStream(
      this.baseUrl(),
      companion.sessionId,
      (debates) => {
        this.debates.set(debates);
      },
      (lifecycle) => {
        this.lifecycle.update((rows) => [lifecycle, ...rows].slice(0, 100));
        if (this.isMemoryLifecycleEvent(lifecycle.eventType)) {
          this.bumpMemoryExplorerRefresh();
        }
      },
      () => undefined,
    );
  }

  onMemoryExplorerViewChanged(view: MemoryView): void {
    this.memoryExplorerView.set(view);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { memoryView: view },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  setMemoryTabMode(mode: 'timeline' | 'graph'): void {
    this.memoryTabMode.set(mode);
    this.syncTimelineQueryToUrl();
  }

  isMemoryTabMode(mode: 'timeline' | 'graph'): boolean {
    return this.memoryTabMode() === mode;
  }

  timelineKindLabel(kind: MemoryTimelineKind): string {
    switch (kind) {
      case 'semantic':
        return 'Claim';
      case 'episodic':
        return 'Episode';
      case 'self':
        return 'Preference';
      case 'procedural':
        return 'Routine';
      case 'scheduled':
        return 'Scheduled';
      case 'debate':
        return 'Debate';
      case 'tool':
        return 'Tool';
      default:
        return 'Memory';
    }
  }

  setTimelineFilterKind(kind: 'all' | MemoryTimelineKind): void {
    this.timelineFilterKind.set(kind);
    this.timelinePage.set(1);
    this.syncTimelineQueryToUrl();
    void this.refreshMemoryTimelineForCurrentCompanion();
  }

  setTimelineFilterLinked(mode: 'all' | 'linked' | 'unlinked'): void {
    this.timelineFilterLinked.set(mode);
    this.timelinePage.set(1);
    this.syncTimelineQueryToUrl();
    void this.refreshMemoryTimelineForCurrentCompanion();
  }

  setTimelineFilterQuery(value: string): void {
    this.timelineFilterQuery.set(value ?? '');
    this.timelinePage.set(1);
    this.syncTimelineQueryToUrl();
    void this.refreshMemoryTimelineForCurrentCompanion();
  }

  setTimelinePageSize(size: number): void {
    const normalized = Number(size) || 20;
    this.timelinePageSize.set(Math.max(10, Math.min(100, normalized)));
    this.timelinePage.set(1);
    this.syncTimelineQueryToUrl();
    void this.refreshMemoryTimelineForCurrentCompanion();
  }

  previousTimelinePage(): void {
    this.timelinePage.set(Math.max(1, this.timelinePage() - 1));
    this.syncTimelineQueryToUrl();
    void this.refreshMemoryTimelineForCurrentCompanion();
  }

  nextTimelinePage(): void {
    this.timelinePage.set(Math.min(this.timelineTotalPages(), this.timelinePage() + 1));
    this.syncTimelineQueryToUrl();
    void this.refreshMemoryTimelineForCurrentCompanion();
  }

  timelineRelationshipsExpanded(itemKey: string): boolean {
    return this.expandedTimelineRelations()[itemKey] === true;
  }

  toggleTimelineRelationships(itemKey: string): void {
    this.expandedTimelineRelations.update((state) => ({
      ...state,
      [itemKey]: !(state[itemKey] === true),
    }));
  }

  private async loadToolEvidenceForTurn(turnIndex: number): Promise<void> {
    const companion = this.companion();
    if (!companion) {
      return;
    }

    const query = new URLSearchParams();
    query.set('companionId', companion.companionId);
    query.set('take', '100');
    const rows = await firstValueFrom(
      this.http.get<ToolInvocationAudit[]>(`${this.baseUrl()}/tool-invocations?${query.toString()}`),
    );
    this.updateAssistantTurn(turnIndex, (turn) => ({ ...turn, toolCalls: rows.slice(0, 20) }));
  }

  private processSseBuffer(buffer: string, assistantIndex: number): string {
    while (true) {
      const idx = buffer.indexOf('\n\n');
      if (idx < 0) {
        break;
      }

      const frame = buffer.slice(0, idx);
      buffer = buffer.slice(idx + 2);
      this.processSseFrame(frame, assistantIndex);
    }

    return buffer;
  }

  private processSseFrame(frame: string, assistantIndex: number): void {
    if (!frame.trim()) {
      return;
    }

    const lines = frame.replace(/\r\n/g, '\n').split('\n');
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

    const payload = dataLines.join('\n');
    let parsed: WorkspaceRawStreamChunk;
    try {
      parsed = JSON.parse(payload) as WorkspaceRawStreamChunk;
    } catch {
      return;
    }

    if (eventName === 'error') {
      const message = parsed.message?.trim() || 'Stream generation failed.';
      const detail = parsed.detail?.trim();
      const combined = detail ? `${message} ${detail}` : message;
      this.updateAssistantTurn(assistantIndex, (turn) => ({
        ...turn,
        text: turn.text.trim().length > 0 ? `${turn.text}\n\n${combined}` : combined,
      }));
      return;
    }

    const delta = parsed.delta ?? parsed.Delta ?? '';
    if (eventName === 'delta' && delta.length > 0) {
      this.updateAssistantTurn(assistantIndex, (turn) => ({ ...turn, text: `${turn.text}${delta}` }));
    }
  }

  setMessageInput(value: string): void {
    this.messageInput.set(value);
  }

  setEvidenceSearch(value: string): void {
    this.evidenceSearch.set(value ?? '');
  }

  setEvidenceTypeFilter(value: string): void {
    this.evidenceTypeFilter.set((value ?? 'all').trim() || 'all');
  }

  toggleEvidenceGroup(eventType: string): void {
    this.evidenceExpandedGroups.update((state) => ({
      ...state,
      [eventType]: !(state[eventType] ?? true),
    }));
  }

  evidenceGroupExpanded(eventType: string): boolean {
    return this.evidenceExpandedGroups()[eventType] ?? true;
  }

  selectEvidenceEvent(eventId: string): void {
    this.selectedEvidenceEventId.update((current) => (current === eventId ? null : eventId));
  }

  isEvidenceEventSelected(eventId: string): boolean {
    return this.selectedEvidenceEventId() === eventId;
  }

  evidenceStatusTone(status: string | null | undefined): 'ok' | 'error' | 'pending' {
    const normalized = (status ?? '').toString().trim().toLowerCase();
    if (normalized.includes('published') || normalized.includes('completed') || normalized.includes('ok') || normalized.includes('success')) {
      return 'ok';
    }
    if (normalized.includes('failed') || normalized.includes('error')) {
      return 'error';
    }
    return 'pending';
  }

  assistantDisplayHtml(text: string): string {
    const source = text ?? '';
    const cached = this.markdownCache.get(source);
    if (cached !== undefined) {
      return cached;
    }

    const parsed = marked.parse(source, { async: false });
    const html = typeof parsed === 'string' ? parsed : source;
    this.markdownCache.set(source, html);
    if (this.markdownCache.size > this.markdownCacheLimit) {
      const firstKey = this.markdownCache.keys().next().value;
      if (typeof firstKey === 'string') {
        this.markdownCache.delete(firstKey);
      }
    }

    return html;
  }

  private updateAssistantTurn(index: number, update: (turn: WorkspaceTurn) => WorkspaceTurn): void {
    this.turns.update((rows) => {
      const current = rows[index];
      if (!current) {
        return rows;
      }

      const clone = rows.slice();
      clone[index] = update(current);
      return clone;
    });
  }

  private toError(error: unknown): string {
    if (error instanceof Error && error.message.trim().length > 0) {
      return error.message;
    }

    if (typeof error === 'string' && error.trim().length > 0) {
      return error;
    }

    try {
      const raw = JSON.stringify(error);
      return raw && raw !== '{}' ? raw : 'Unknown error';
    } catch {
      return 'Unknown error';
    }
  }

  private baseUrl(): string {
    const trimmed = this.context.apiBase().trim();
    if (!trimmed) {
      return '/api';
    }

    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
  }

  private applyMemoryViewFromQuery(raw: string | null): void {
    const normalized = (raw ?? '').trim().toLowerCase();
    const view: MemoryView = normalized === 'relationships' || normalized === 'workbench' ? normalized : 'graph';
    this.memoryExplorerView.set(view);
  }

  private applyTimelineQueryFromParams(params: { get(name: string): string | null }): void {
    const modeRaw = (params.get('memoryMode') ?? '').trim().toLowerCase();
    if (modeRaw === 'timeline' || modeRaw === 'graph') {
      this.memoryTabMode.set(modeRaw);
    }

    const kindRaw = (params.get('timelineKind') ?? '').trim().toLowerCase();
    const kind = this.isTimelineKind(kindRaw) ? kindRaw : 'all';
    this.timelineFilterKind.set(kind);

    const linkedRaw = (params.get('timelineLinked') ?? '').trim().toLowerCase();
    const linked = linkedRaw === 'linked' || linkedRaw === 'unlinked' ? linkedRaw : 'all';
    this.timelineFilterLinked.set(linked);

    this.timelineFilterQuery.set(params.get('timelineQuery') ?? '');

    const pageRaw = Number(params.get('timelinePage') ?? '1');
    this.timelinePage.set(Number.isFinite(pageRaw) ? Math.max(1, Math.floor(pageRaw)) : 1);

    const sizeRaw = Number(params.get('timelinePageSize') ?? '20');
    this.timelinePageSize.set(Number.isFinite(sizeRaw) ? Math.max(10, Math.min(100, Math.floor(sizeRaw))) : 20);
  }

  private syncTimelineQueryToUrl(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        memoryMode: this.memoryTabMode(),
        timelineKind: this.timelineFilterKind(),
        timelineLinked: this.timelineFilterLinked(),
        timelineQuery: this.timelineFilterQuery().trim() || null,
        timelinePage: this.timelinePage(),
        timelinePageSize: this.timelinePageSize(),
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  private isTimelineKind(raw: string): raw is MemoryTimelineKind | 'all' {
    return raw === 'all'
      || raw === 'semantic'
      || raw === 'episodic'
      || raw === 'self'
      || raw === 'procedural'
      || raw === 'scheduled'
      || raw === 'debate'
      || raw === 'tool'
      || raw === 'unknown';
  }

  private bumpMemoryExplorerRefresh(): void {
    this.memoryExplorerRefreshKey.update((value) => value + 1);
  }

  private hasMemoryImpactEvents(rows: EventingRow[]): boolean {
    return rows.slice(0, 30).some((row) => {
      const eventType = (row.eventType ?? '').toLowerCase();
      const aggregateType = (row.aggregateType ?? '').toLowerCase();
      return aggregateType === 'memoryrelationship'
        || eventType.includes('memoryrelationship')
        || eventType.includes('semantic')
        || eventType.includes('episodic')
        || eventType.includes('procedural')
        || eventType.includes('selfpreference')
        || eventType.includes('subconsciousmemoryupdate');
    });
  }

  private isMemoryLifecycleEvent(eventType: string | null | undefined): boolean {
    const normalized = (eventType ?? '').toLowerCase();
    return normalized.includes('subconsciousmemoryupdate')
      || normalized.includes('subconsciousdebateconcluded');
  }

  private async refreshMemoryTimeline(companion: CompanionProfile): Promise<void> {
    this.memoryTimelineLoading.set(true);
    try {
      const base = this.baseUrl();
      const query = new URLSearchParams();
      query.set('page', String(this.timelinePage()));
      query.set('pageSize', String(this.timelinePageSize()));
      query.set('kind', this.timelineFilterKind());
      query.set('linked', this.timelineFilterLinked());
      if (this.timelineFilterQuery().trim().length > 0) {
        query.set('query', this.timelineFilterQuery().trim());
      }

      const response = await firstValueFrom(
        this.http.get<MemoryTimelinePageResponse>(`${base}/workspace/companion/${encodeURIComponent(companion.companionId)}/memory-timeline?${query.toString()}`),
      );

      this.memoryTimelineItems.set(Array.isArray(response?.items) ? response.items : []);
      this.memoryTimelineTotal.set(Math.max(0, Number(response?.total) || 0));
      this.expandedTimelineRelations.set({});
    } catch {
      this.memoryTimelineItems.set([]);
      this.memoryTimelineTotal.set(0);
      this.expandedTimelineRelations.set({});
    } finally {
      this.memoryTimelineLoading.set(false);
    }
  }

  private async refreshMemoryTimelineForCurrentCompanion(): Promise<void> {
    const companion = this.companion();
    if (!companion) {
      return;
    }

    await this.refreshMemoryTimeline(companion);
  }
}
