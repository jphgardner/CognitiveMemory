import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MemoryRelationshipRow, MemoryView, RelationshipGraphEdge, RelationshipGraphNode } from './models/console.models';
import {
  WorkspaceMemoryNodeDialogComponent,
  WorkspaceMemoryNodeDialogData,
  WorkspaceMemoryNodeDialogRelationship,
} from './workspace-memory-node-dialog.component';
import { AppContextService } from './services/app-context.service';
import { MemoryNodeDetail, MemoryStateService } from './services/memory-state.service';

type ExplorerTone = 'idle' | 'busy' | 'ok' | 'error';
interface MemoryClaimPreview {
  claimId: string;
  predicate: string;
  value: string;
  confidence: number;
  status: string;
  updatedAtUtc: string;
}

@Component({
  selector: 'app-workspace-memory-explorer',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule],
  template: `
    <article class="memory-shell">
      <header class="memory-header">
        <div>
          <p class="eyebrow">Workspace Memory</p>
          <h3>Memory Explorer</h3>
          <p class="subtle">Inspect, query, and shape your companion's relationship graph.</p>
        </div>
        <div class="header-actions">
          <button mat-stroked-button type="button" [class.active]="isMemoryView('graph')" (click)="setMemoryView('graph')">Graph</button>
          <button mat-stroked-button type="button" [class.active]="isMemoryView('relationships')" (click)="setMemoryView('relationships')">Relationships</button>
          <button mat-stroked-button type="button" [class.active]="isMemoryView('workbench')" (click)="setMemoryView('workbench')">Workbench</button>
          <button mat-stroked-button type="button" (click)="loadRelationshipsBySession()">Refresh</button>
        </div>
      </header>

      <section class="status-row">
        <div class="status-pill">
          <span class="dot"></span>
          <span>Memory state</span>
        </div>
        <span class="subtle">{{ graphSummaryText() }}</span>
      </section>

      <div class="kpi-grid memory-kpis">
        <article class="kpi-card">
          <p class="label">Total</p>
          <h3>{{ relationships.length }}</h3>
        </article>
        <article class="kpi-card">
          <p class="label">Active</p>
          <h3>{{ relationshipCountByStatus('active') }}</h3>
        </article>
        <article class="kpi-card">
          <p class="label">Retired</p>
          <h3>{{ relationshipCountByStatus('retired') }}</h3>
        </article>
        <article class="kpi-card">
          <p class="label">Visible</p>
          <h3>{{ currentRelationshipRows().length }}</h3>
        </article>
      </div>
    </article>

    <article class="memory-panel" *ngIf="isMemoryView('graph')">
      <div class="panel-head">
        <h3>Relationship Graph</h3>
        <span>{{ relationshipGraphEdges().length }} visible edges</span>
      </div>

      <div class="memory-grid">
        <section class="graph-zone">
          <div class="graph-canvas" *ngIf="relationshipGraphNodes().length > 0; else noGraphData">
            <svg [attr.viewBox]="'0 0 ' + graphWidth + ' ' + graphHeight" preserveAspectRatio="xMidYMid meet">
              <g>
                <line
                  *ngFor="let edge of relationshipGraphRenderedEdges()"
                  [attr.x1]="edge.x1"
                  [attr.y1]="edge.y1"
                  [attr.x2]="edge.x2"
                  [attr.y2]="edge.y2"
                  [attr.class]="relationshipEdgeClass(edge)">
                </line>
              </g>
              <g>
                <g *ngFor="let node of relationshipGraphNodes()">
                  <circle
                    [attr.cx]="node.x"
                    [attr.cy]="node.y"
                    [attr.r]="relationshipNodeRadius(node)"
                    [attr.class]="relationshipNodeClass(node)"
                    (click)="selectGraphNode(node.key)">
                  </circle>
                  <text [attr.x]="node.x + 10" [attr.y]="node.y - 10" class="graph-label">{{ relationshipNodeLabel(node) }}</text>
                </g>
              </g>
            </svg>
          </div>
          <ng-template #noGraphData>
            <div class="empty-panel">
              <h4>No graph edges yet</h4>
              <p>Load this session or use Workbench to create the first relationship.</p>
            </div>
          </ng-template>
        </section>

        <aside class="graph-side">
          <div class="side-card">
            <div class="panel-head">
              <h3>Selection</h3>
              <div class="row">
                <button mat-button type="button" (click)="openSelectedNodeDialog()" [disabled]="!selectedGraphNodeKey">View</button>
                <button mat-button type="button" (click)="clearGraphSelection()" [disabled]="!selectedGraphNodeKey">Clear</button>
              </div>
            </div>
            <ng-container *ngIf="selectedGraphNode() as node; else noSelection">
              <p class="selection-title">{{ relationshipNodeLabel(node) }}</p>
              <p class="subtle">Degree: {{ node.degree }}</p>
              <p class="subtle selection-preview">{{ selectedNodeValue(node) }}</p>
            </ng-container>
            <ng-template #noSelection>
              <p class="subtle">Select a node to inspect direct neighborhood edges.</p>
            </ng-template>
          </div>

          <div class="side-card">
            <div class="panel-head">
              <h3>Visible Edge Types</h3>
            </div>
            <div class="chip-row">
              <span class="pill" *ngFor="let rel of currentRelationshipRows().slice(0, 8)">{{ rel.relationshipType }}</span>
            </div>
            <p class="subtle" *ngIf="currentRelationshipRows().length === 0">No edges match current filters.</p>
          </div>
        </aside>
      </div>
    </article>

    <article class="memory-panel" *ngIf="isMemoryView('graph') || isMemoryView('relationships')">
      <div class="panel-head">
        <h3>Relationship Explorer</h3>
        <span>{{ currentRelationshipRows().length }} edges</span>
      </div>

      <div class="filter-grid">
        <label>
          <span class="label">Type</span>
          <input [(ngModel)]="relationshipTypeFilter" name="relationshipTypeFilterWs" placeholder="supports / contradicts / about" />
        </label>
        <label>
          <span class="label">Search</span>
          <input [(ngModel)]="relationshipSearch" name="relationshipSearchWs" placeholder="from/to/type/id" />
        </label>
        <label>
          <span class="label">Status</span>
          <select [(ngModel)]="relationshipStatusFilter" name="relationshipStatusFilterWs">
            <option value="all">All</option>
            <option value="active">Active</option>
            <option value="retired">Retired</option>
          </select>
        </label>
        <label>
          <span class="label">Sort</span>
          <select [(ngModel)]="relationshipSort" name="relationshipSortWs">
            <option value="updated">Newest</option>
            <option value="confidence">Confidence</option>
            <option value="strength">Strength</option>
          </select>
        </label>
        <label>
          <span class="label">Take</span>
          <input type="number" [(ngModel)]="relationshipTake" name="relationshipTakeWs" min="1" max="1000" />
        </label>
        <button mat-stroked-button type="button" (click)="loadRelationshipsBySession()">Load Session</button>
      </div>

      <div class="relationship-list" *ngIf="currentRelationshipRows().length > 0; else noSelectedGraphEdges">
        <article *ngFor="let rel of currentRelationshipRows()" class="edge-row" [ngClass]="relationshipStatusClass(rel)">
          <div class="edge-row-main">
            <div class="edge-head">
              <strong>{{ rel.relationshipType }}</strong>
              <span>{{ rel.updatedAtUtc | date: 'yyyy-MM-dd HH:mm:ss' }}</span>
            </div>
            <p class="edge-link">{{ relationshipNodeTypeLabel(rel.fromType) }}:{{ rel.fromId }} <span>-></span> {{ relationshipNodeTypeLabel(rel.toType) }}:{{ rel.toId }}</p>
            <div class="chip-row">
              <span class="pill">{{ relationshipStatusLabel(rel) }}</span>
              <span class="pill">conf {{ rel.confidence | number:'1.2-2' }}</span>
              <span class="pill">str {{ rel.strength | number:'1.2-2' }}</span>
            </div>
          </div>
          <div class="edge-row-actions" *ngIf="isMemoryView('relationships')">
            <button mat-button type="button" (click)="retireRelationship(rel.relationshipId)" [disabled]="relationshipStatusKey(rel.status) === 'retired'">Retire</button>
          </div>
        </article>
      </div>
      <ng-template #noSelectedGraphEdges>
        <div class="empty-panel">
          <h4>No matching relationships</h4>
          <p>Try widening filters or loading the full session graph.</p>
        </div>
      </ng-template>
    </article>

    <article class="memory-panel" *ngIf="isMemoryView('workbench')">
      <div class="panel-head">
        <h3>Memory Workbench</h3>
        <span>Create, inspect, and maintain graph edges</span>
      </div>

      <section class="workbench-block">
        <h4>Load Node Neighborhood</h4>
        <div class="grid-2 query">
          <label>
            <span class="label">Node Type</span>
            <select [(ngModel)]="relationshipByNodeType" name="relationshipByNodeTypeWs">
              <option *ngFor="let type of relationshipNodeTypes" [ngValue]="type.value">{{ type.label }}</option>
            </select>
          </label>
          <label>
            <span class="label">Node ID</span>
            <input [(ngModel)]="relationshipByNodeId" name="relationshipByNodeIdWs" placeholder="claimId / routineId / key" />
          </label>
        </div>
        <div class="row">
          <button mat-stroked-button type="button" (click)="loadRelationshipsByNode()" [disabled]="!relationshipByNodeId.trim()">Load Node</button>
        </div>
      </section>

      <section class="workbench-block">
        <h4>Upsert Relationship</h4>
        <div class="grid-2 query">
          <label>
            <span class="label">From Type</span>
            <select [(ngModel)]="relationshipCreateFromType" name="relationshipCreateFromTypeWs">
              <option *ngFor="let type of relationshipNodeTypes" [ngValue]="type.value">{{ type.label }}</option>
            </select>
          </label>
          <label><span class="label">From ID</span><input [(ngModel)]="relationshipCreateFromId" name="relationshipCreateFromIdWs" /></label>
          <label>
            <span class="label">To Type</span>
            <select [(ngModel)]="relationshipCreateToType" name="relationshipCreateToTypeWs">
              <option *ngFor="let type of relationshipNodeTypes" [ngValue]="type.value">{{ type.label }}</option>
            </select>
          </label>
          <label><span class="label">To ID</span><input [(ngModel)]="relationshipCreateToId" name="relationshipCreateToIdWs" /></label>
          <label><span class="label">Relation</span><input [(ngModel)]="relationshipCreateType" name="relationshipCreateTypeWs" /></label>
          <label><span class="label">Confidence</span><input type="number" min="0" max="1" step="0.05" [(ngModel)]="relationshipCreateConfidence" name="relationshipCreateConfidenceWs" /></label>
          <label><span class="label">Strength</span><input type="number" min="0" max="1" step="0.05" [(ngModel)]="relationshipCreateStrength" name="relationshipCreateStrengthWs" /></label>
        </div>
        <div class="row">
          <button mat-raised-button color="primary" type="button" (click)="createRelationship()">Upsert Relationship</button>
        </div>
      </section>

      <section class="workbench-block">
        <h4>Maintenance</h4>
        <label>
          <span class="label">Backfill Take</span>
          <input type="number" [(ngModel)]="relationshipBackfillTake" name="relationshipBackfillTakeWs" min="100" max="10000" />
        </label>
        <label>
          <span class="label">AI Extract Take</span>
          <input type="number" [(ngModel)]="relationshipExtractTake" name="relationshipExtractTakeWs" min="20" max="2000" />
        </label>
        <label class="decision-checkbox">
          <input type="checkbox" [(ngModel)]="relationshipExtractApply" name="relationshipExtractApplyWs" />
          <span>Apply AI edges</span>
        </label>
        <div class="row">
          <button mat-stroked-button type="button" (click)="backfillRelationships()">Run Backfill</button>
          <button mat-stroked-button type="button" (click)="extractRelationshipsWithAi()">Run AI Extractor</button>
        </div>
      </section>
    </article>

  `,
  styles: [`
    .memory-shell {
      position: relative;
      border: 1px solid rgba(148, 163, 184, 0.22);
      border-radius: 1rem;
      background: linear-gradient(180deg, rgba(17, 24, 39, 0.1), rgba(17, 24, 39, 0.04));
      padding: 1rem;
      box-shadow: 0 14px 32px rgba(15, 23, 42, 0.2);
    }
    .memory-header {
      display: flex;
      justify-content: space-between;
      gap: 0.9rem;
      align-items: flex-start;
      flex-wrap: wrap;
    }
    .memory-header h3 {
      margin: 0;
      font-size: 1.15rem;
      letter-spacing: 0.01em;
    }
    .eyebrow {
      margin: 0 0 0.2rem;
      font-size: 0.72rem;
      text-transform: uppercase;
      letter-spacing: 0.12em;
      opacity: 0.8;
    }
    .subtle {
      margin: 0;
      opacity: 0.82;
      font-size: 0.87rem;
    }
    .header-actions {
      display: flex;
      gap: 0.4rem;
      flex-wrap: wrap;
      align-items: center;
    }
    .status-row {
      margin-top: 0.8rem;
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 0.5rem;
      flex-wrap: wrap;
    }
    .status-pill {
      display: inline-flex;
      align-items: center;
      gap: 0.45rem;
      border-radius: 999px;
      border: 1px solid rgba(148, 163, 184, 0.35);
      padding: 0.3rem 0.55rem;
      font-size: 0.8rem;
      background: rgba(15, 23, 42, 0.28);
    }
    .status-pill .dot {
      width: 0.45rem;
      height: 0.45rem;
      border-radius: 999px;
      background: rgba(148, 163, 184, 0.9);
    }
    .pill-ok .dot {
      background: rgba(16, 185, 129, 0.95);
    }
    .pill-fail .dot {
      background: rgba(239, 68, 68, 0.95);
    }
    .pill-busy .dot {
      background: rgba(59, 130, 246, 0.95);
    }
    .memory-kpis {
      margin-top: 0.85rem;
      grid-template-columns: repeat(4, minmax(0, 1fr));
    }
    .memory-panel {
      margin-top: 0.9rem;
      border: 1px solid rgba(148, 163, 184, 0.24);
      border-radius: 1rem;
      background: rgba(15, 23, 42, 0.2);
      padding: 0.95rem;
      box-shadow: 0 10px 26px rgba(15, 23, 42, 0.16);
    }
    .memory-grid {
      display: grid;
      gap: 0.9rem;
      grid-template-columns: minmax(0, 2.15fr) minmax(0, 1fr);
      align-items: start;
    }
    .graph-zone {
      min-height: 340px;
    }
    .graph-side {
      display: grid;
      gap: 0.72rem;
    }
    .side-card {
      border: 1px solid rgba(148, 163, 184, 0.22);
      border-radius: 0.8rem;
      background: rgba(2, 6, 23, 0.32);
      padding: 0.7rem;
    }
    .selection-title {
      margin: 0;
      font-weight: 650;
      line-height: 1.3;
      word-break: break-word;
    }
    .selection-preview {
      margin-top: 0.45rem;
      line-height: 1.42;
      word-break: break-word;
      max-height: 4.2rem;
      overflow: hidden;
    }
    .chip-row {
      display: flex;
      gap: 0.35rem;
      flex-wrap: wrap;
    }
    .filter-grid {
      display: grid;
      grid-template-columns: repeat(6, minmax(0, 1fr));
      gap: 0.65rem;
      align-items: end;
    }
    .relationship-list {
      margin-top: 0.78rem;
      display: grid;
      gap: 0.55rem;
      max-height: 520px;
      overflow: auto;
      padding-right: 0.2rem;
    }
    .edge-row {
      border: 1px solid rgba(148, 163, 184, 0.2);
      border-radius: 0.7rem;
      background: rgba(15, 23, 42, 0.35);
      padding: 0.72rem 0.78rem;
      display: flex;
      justify-content: space-between;
      gap: 0.75rem;
      align-items: flex-start;
    }
    .edge-row-main {
      min-width: 0;
      flex: 1;
    }
    .edge-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      font-size: 0.82rem;
    }
    .edge-link {
      margin: 0.35rem 0 0.45rem;
      word-break: break-word;
      opacity: 0.94;
    }
    .edge-link span {
      opacity: 0.7;
    }
    .edge-row-actions {
      display: flex;
      align-items: center;
    }
    .workbench-block {
      border: 1px solid rgba(148, 163, 184, 0.2);
      border-radius: 0.8rem;
      background: rgba(2, 6, 23, 0.28);
      padding: 0.72rem;
      margin-bottom: 0.72rem;
    }
    .workbench-block h4 {
      margin: 0 0 0.62rem;
      font-size: 0.92rem;
      letter-spacing: 0.01em;
    }
    .empty-panel {
      border: 1px dashed rgba(148, 163, 184, 0.4);
      border-radius: 0.8rem;
      padding: 1rem;
      text-align: center;
      opacity: 0.92;
    }
    .empty-panel h4 {
      margin: 0 0 0.3rem;
    }
    @media (max-width: 1260px) {
      .filter-grid {
        grid-template-columns: repeat(3, minmax(0, 1fr));
      }
      .memory-grid {
        grid-template-columns: 1fr;
      }
    }
    @media (max-width: 960px) {
      .memory-kpis {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
      .filter-grid {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
      .edge-row {
        flex-direction: column;
      }
    }
    @media (max-width: 640px) {
      .filter-grid {
        grid-template-columns: 1fr;
      }
      .memory-header {
        flex-direction: column;
      }
    }
  `],
})
export class WorkspaceMemoryExplorerComponent implements OnChanges, OnDestroy {
  private readonly memoryApi = inject(MemoryStateService);
  private readonly context = inject(AppContextService);
  private readonly dialog = inject(MatDialog);

  @Input() sessionId: string | null = null;
  @Input() companionId: string | null = null;
  @Input() claims: MemoryClaimPreview[] = [];
  @Input() set disabled(value: boolean) {
    this.setDisabledState(value);
  }
  get disabled(): boolean {
    return this.disabledView;
  }
  @Input() initialView: MemoryView = 'graph';
  @Input() autoRefreshKey = 0;
  @Output() readonly viewChange = new EventEmitter<MemoryView>();

  busy = false;
  tone: ExplorerTone = 'idle';
  statusText = 'Ready.';
  private disabledInternal = false;
  private disabledView = false;
  private disabledSyncQueued = false;
  private busyInternal = false;
  private busySyncQueued = false;
  private pendingTone: ExplorerTone = 'idle';
  private pendingStatusText = 'Ready.';
  private statusSyncQueued = false;

  relationships: MemoryRelationshipRow[] = [];
  memoryView: MemoryView = 'graph';
  relationshipByNodeType = 0;
  relationshipByNodeId = '';
  relationshipTypeFilter = '';
  relationshipSearch = '';
  relationshipStatusFilter: 'all' | 'active' | 'retired' = 'all';
  relationshipSort: 'updated' | 'confidence' | 'strength' = 'updated';
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
  graphWidth = 980;
  graphHeight = 520;
  private lastAutoRefreshAt = 0;
  private pendingRefreshTimer: ReturnType<typeof setTimeout> | null = null;
  private busySyncTimer: ReturnType<typeof setTimeout> | null = null;
  private statusSyncTimer: ReturnType<typeof setTimeout> | null = null;
  private disabledSyncTimer: ReturnType<typeof setTimeout> | null = null;
  private relationshipsSyncTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly nodeDetailCache = new Map<string, MemoryNodeDetail>();
  private nodeDetailEndpointAvailable = true;

  readonly relationshipNodeTypes = [
    { value: 0, label: 'SemanticClaim' },
    { value: 1, label: 'EpisodicEvent' },
    { value: 2, label: 'ProceduralRoutine' },
    { value: 3, label: 'SelfPreference' },
    { value: 4, label: 'ScheduledAction' },
    { value: 5, label: 'SubconsciousDebate' },
    { value: 6, label: 'ToolInvocation' },
  ];

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['initialView']) {
      const view = this.initialView;
      if (view === 'graph' || view === 'relationships' || view === 'workbench') {
        this.memoryView = view;
      }
    }

    if (changes['sessionId']) {
      this.selectedGraphNodeKey = null;
      this.nodeDetailCache.clear();
      if (this.normalizedSessionId()) {
        this.scheduleLoadRelationshipsBySession();
      } else {
        this.setRelationshipsState([]);
      }
    }

    if (changes['companionId'] && !changes['companionId'].firstChange) {
      this.nodeDetailCache.clear();
    }

    if (changes['autoRefreshKey'] && !changes['autoRefreshKey'].firstChange) {
      this.scheduleExternalRefreshSignal();
    }
  }

  ngOnDestroy(): void {
    this.clearPendingRefresh();
    this.clearStateSyncTimers();
  }

  setMemoryView(view: MemoryView): void {
    if (this.memoryView === view) {
      return;
    }
    this.memoryView = view;
    this.viewChange.emit(view);
  }

  isMemoryView(view: MemoryView): boolean {
    return this.memoryView === view;
  }

  async loadRelationshipsBySession(): Promise<void> {
    const sessionId = this.normalizedSessionId();
    const companionId = this.normalizedCompanionId();
    if (!sessionId || !companionId) {
      this.setRelationshipsState([]);
      this.setStatus('error', 'Companion and session context are required.');
      return;
    }

    await this.runBusy('Loading memory relationships...', async () => {
      const rows = await this.memoryApi.loadBySession(
        this.baseUrl(),
        companionId,
        sessionId,
        this.relationshipTake,
        this.relationshipTypeFilter,
      );
      this.setRelationshipsState(rows);
      this.setStatus('ok', `Loaded ${rows.length} relationship(s).`);
    });
  }

  async loadRelationshipsByNode(): Promise<void> {
    const sessionId = this.normalizedSessionId();
    const companionId = this.normalizedCompanionId();
    const nodeId = this.relationshipByNodeId.trim();
    if (!sessionId || !nodeId || !companionId) {
      this.setStatus('error', 'Companion, session ID and Node ID are required.');
      return;
    }

    await this.runBusy('Loading node relationships...', async () => {
      const rows = await this.memoryApi.loadByNode(
        this.baseUrl(),
        companionId,
        sessionId,
        this.relationshipByNodeType,
        nodeId,
        this.relationshipTake,
        this.relationshipTypeFilter,
      );
      this.setRelationshipsState(rows);
      this.setStatus('ok', `Loaded ${rows.length} relationship(s) for node.`);
    });
  }

  async createRelationship(): Promise<void> {
    const sessionId = this.normalizedSessionId();
    const companionId = this.normalizedCompanionId();
    const fromId = this.relationshipCreateFromId.trim();
    const toId = this.relationshipCreateToId.trim();
    const relationshipType = this.relationshipCreateType.trim();
    if (!sessionId || !fromId || !toId || !relationshipType || !companionId) {
      this.setStatus('error', 'Companion, session ID, From ID, To ID and relationship type are required.');
      return;
    }

    await this.runBusy('Creating memory relationship...', async () => {
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
    });
  }

  async retireRelationship(relationshipId: string): Promise<void> {
    if (!relationshipId) {
      return;
    }

    await this.runBusy('Retiring relationship...', async () => {
      await this.memoryApi.retire(this.baseUrl(), relationshipId);
      this.setStatus('ok', 'Relationship retired.');
      await this.loadRelationshipsBySession();
    });
  }

  async backfillRelationships(): Promise<void> {
    const sessionId = this.normalizedSessionId();
    await this.runBusy('Running relationship backfill...', async () => {
      await this.memoryApi.backfill(this.baseUrl(), sessionId || null, this.relationshipBackfillTake);
      this.setStatus('ok', 'Relationship backfill completed.');
      await this.loadRelationshipsBySession();
    });
  }

  async extractRelationshipsWithAi(): Promise<void> {
    const sessionId = this.normalizedSessionId();
    if (!sessionId) {
      this.setStatus('error', 'Session ID is required.');
      return;
    }

    await this.runBusy('Running AI relationship extraction...', async () => {
      await this.memoryApi.extract(this.baseUrl(), sessionId, this.relationshipExtractTake, this.relationshipExtractApply);
      this.setStatus('ok', this.relationshipExtractApply ? 'AI extraction applied.' : 'AI extraction dry-run completed.');
      await this.loadRelationshipsBySession();
    });
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
            status: this.relationshipStatusLabel(rel),
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
        .slice(0, 40);
    }

    return this.relationships
      .filter((rel) => this.isRelationshipVisibleInGraph(rel))
      .filter((rel) => this.relationshipNodeKey(rel.fromType, rel.fromId) === selected || this.relationshipNodeKey(rel.toType, rel.toId) === selected)
      .slice(0, 80);
  }

  selectedNodeConnectedRelationships(): MemoryRelationshipRow[] {
    const node = this.selectedGraphNode();
    if (!node) {
      return [];
    }

    const nodeKey = node.key;
    return this.relationships
      .filter((rel) => this.isRelationshipVisibleInGraph(rel))
      .filter((rel) => this.relationshipNodeKey(rel.fromType, rel.fromId) === nodeKey || this.relationshipNodeKey(rel.toType, rel.toId) === nodeKey)
      .sort((a, b) => new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime())
      .slice(0, 12);
  }

  selectedNodeValue(node: RelationshipGraphNode): string {
    const fromClaim = this.claims.find((claim) => claim.claimId.toLowerCase() === node.nodeId.toLowerCase());
    if (fromClaim) {
      return `${fromClaim.predicate}: ${fromClaim.value}`;
    }

    const firstRelValue = this.selectedNodeConnectedRelationships()
      .map((rel) => this.tryExtractValueFromMetadata(rel))
      .find((value) => value !== null);
    if (firstRelValue) {
      return firstRelValue;
    }

    if (node.nodeType === 3) {
      return `Self preference key: ${node.nodeId}`;
    }

    return node.nodeId;
  }

  selectedNodeRelationshipPeerLabel(rel: MemoryRelationshipRow, selectedKey: string): string {
    const fromKey = this.relationshipNodeKey(rel.fromType, rel.fromId);
    if (fromKey === selectedKey) {
      return `${this.relationshipNodeTypeLabel(rel.toType)}:${rel.toId}`;
    }

    return `${this.relationshipNodeTypeLabel(rel.fromType)}:${rel.fromId}`;
  }

  selectedNodeRelationshipValue(rel: MemoryRelationshipRow, selectedKey: string): string {
    const fromKey = this.relationshipNodeKey(rel.fromType, rel.fromId);
    const peerType = fromKey === selectedKey ? rel.toType : rel.fromType;
    const peerId = fromKey === selectedKey ? rel.toId : rel.fromId;
    const peerClaim = this.claims.find((claim) => claim.claimId.toLowerCase() === String(peerId).toLowerCase());
    if (peerClaim) {
      return `${peerClaim.predicate}: ${peerClaim.value}`;
    }

    return this.tryExtractValueFromMetadata(rel) ?? `Node ID: ${peerId} (${this.relationshipNodeTypeLabel(peerType)})`;
  }

  async openSelectedNodeDialog(): Promise<void> {
    const node = this.selectedGraphNode();
    if (!node) {
      return;
    }

    try {
      const selectedDetail = await this.resolveNodeDetail(node.nodeType, node.nodeId);
      const relationships = this.selectedNodeConnectedRelationships();

      const peerDetails = await Promise.all(
        relationships.map(async (rel) => {
          const fromKey = this.relationshipNodeKey(rel.fromType, rel.fromId);
          const peerType = fromKey === node.key ? rel.toType : rel.fromType;
          const peerId = fromKey === node.key ? rel.toId : rel.fromId;
          const peerDetail = await this.resolveNodeDetail(peerType, String(peerId));
          return { rel, peerDetail };
        }),
      );

      const data: WorkspaceMemoryNodeDialogData = {
        nodeLabel: selectedDetail?.title?.trim() || this.relationshipNodeLabel(node),
        nodeValue: this.composeDetailValue(selectedDetail, this.selectedNodeValue(node)),
        degree: node.degree,
        relationships: peerDetails.map(
          ({ rel, peerDetail }): WorkspaceMemoryNodeDialogRelationship => ({
            relationshipType: rel.relationshipType,
            peerLabel: peerDetail?.title?.trim() || this.selectedNodeRelationshipPeerLabel(rel, node.key),
            value: this.composeDetailValue(peerDetail, this.selectedNodeRelationshipValue(rel, node.key)),
            updatedAtUtc: rel.updatedAtUtc,
          }),
        ),
      };

      this.dialog.open(WorkspaceMemoryNodeDialogComponent, {
        width: '760px',
        maxWidth: '95vw',
        maxHeight: '88vh',
        autoFocus: false,
        panelClass: 'workspace-insights-dialog',
        data,
      });
    } catch {
      // Keep dialog action non-blocking; fallback values already come from local graph context.
    }
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

  private baseUrl(): string {
    const trimmed = this.context.apiBase().trim();
    if (!trimmed) {
      return '/api';
    }

    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
  }

  private normalizedSessionId(): string {
    return this.sessionId?.trim() ?? '';
  }

  private normalizedCompanionId(): string {
    return this.companionId?.trim() ?? '';
  }

  private relationshipNodeKey(type: number, id: string): string {
    return `${type}:${id}`.toLowerCase();
  }

  private isRelationshipVisibleInGraph(rel: MemoryRelationshipRow): boolean {
    const status = this.relationshipStatusKey(rel.status);
    return status === 'active';
  }

  currentRelationshipRows(): MemoryRelationshipRow[] {
    const source = isMemoryGraphLike(this.memoryView)
      ? this.selectedGraphRelationships()
      : this.relationships;

    const search = this.relationshipSearch.trim().toLowerCase();
    const filtered = source
      .filter((rel) => {
        if (this.relationshipStatusFilter === 'all') {
          return true;
        }

        return this.relationshipStatusKey(rel.status) === this.relationshipStatusFilter;
      })
      .filter((rel) => {
        if (!search) {
          return true;
        }

        const haystack = [
          rel.relationshipType,
          String(rel.fromId),
          String(rel.toId),
          this.relationshipNodeTypeLabel(rel.fromType),
          this.relationshipNodeTypeLabel(rel.toType),
          this.relationshipStatusLabel(rel),
        ].join(' ').toLowerCase();
        return haystack.includes(search);
      });

    return filtered.sort((a, b) => this.sortRelationshipRows(a, b));
  }

  relationshipCountByStatus(status: 'active' | 'retired'): number {
    return this.relationships.filter((rel) => this.relationshipStatusKey(rel.status) === status).length;
  }

  relationshipStatusLabel(rel: MemoryRelationshipRow): string {
    const key = this.relationshipStatusKey(rel.status);
    return key === 'retired' ? 'Retired' : key === 'active' ? 'Active' : 'Unknown';
  }

  relationshipStatusKey(status: unknown): 'active' | 'retired' | 'unknown' {
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

  private setStatus(tone: ExplorerTone, text: string): void {
    this.pendingTone = tone;
    this.pendingStatusText = text;
    if (this.statusSyncQueued) {
      return;
    }

    this.statusSyncQueued = true;
    this.statusSyncTimer = setTimeout(() => {
      this.statusSyncTimer = null;
      this.statusSyncQueued = false;
      this.tone = this.pendingTone;
      this.statusText = this.pendingStatusText;
    });
  }

  private async runBusy(busyText: string, action: () => Promise<void>): Promise<void> {
    if (this.disabledInternal || this.busyInternal) {
      return;
    }

    this.setBusyState(true);
    this.setStatus('busy', busyText);
    try {
      await action();
    } catch (error) {
      this.setStatus('error', this.toError(error));
    } finally {
      this.setBusyState(false);
    }
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

  private onExternalRefreshSignal(): void {
    const now = Date.now();
    if (now - this.lastAutoRefreshAt < 1200) {
      return;
    }

    this.lastAutoRefreshAt = now;
    if (this.disabledInternal || this.busyInternal || !this.normalizedSessionId()) {
      return;
    }

    void this.loadRelationshipsBySession();
  }

  private scheduleLoadRelationshipsBySession(): void {
    this.clearPendingRefresh();
    this.pendingRefreshTimer = setTimeout(() => {
      this.pendingRefreshTimer = null;
      void this.loadRelationshipsBySession();
    });
  }

  private scheduleExternalRefreshSignal(): void {
    this.clearPendingRefresh();
    this.pendingRefreshTimer = setTimeout(() => {
      this.pendingRefreshTimer = null;
      this.onExternalRefreshSignal();
    });
  }

  private clearPendingRefresh(): void {
    if (this.pendingRefreshTimer) {
      clearTimeout(this.pendingRefreshTimer);
      this.pendingRefreshTimer = null;
    }
  }

  private setBusyState(value: boolean): void {
    this.busyInternal = value;
    if (this.busySyncQueued) {
      return;
    }

    this.busySyncQueued = true;
    this.busySyncTimer = setTimeout(() => {
      this.busySyncTimer = null;
      this.busySyncQueued = false;
      this.busy = this.busyInternal;
    });
  }

  private setDisabledState(value: boolean): void {
    this.disabledInternal = value;
    if (this.disabledSyncQueued) {
      return;
    }

    this.disabledSyncQueued = true;
    this.disabledSyncTimer = setTimeout(() => {
      this.disabledSyncTimer = null;
      this.disabledSyncQueued = false;
      this.disabledView = this.disabledInternal;
    });
  }

  private clearStateSyncTimers(): void {
    if (this.busySyncTimer) {
      clearTimeout(this.busySyncTimer);
      this.busySyncTimer = null;
    }

    if (this.statusSyncTimer) {
      clearTimeout(this.statusSyncTimer);
      this.statusSyncTimer = null;
    }

    if (this.disabledSyncTimer) {
      clearTimeout(this.disabledSyncTimer);
      this.disabledSyncTimer = null;
    }

    if (this.relationshipsSyncTimer) {
      clearTimeout(this.relationshipsSyncTimer);
      this.relationshipsSyncTimer = null;
    }
  }

  private setRelationshipsState(rows: MemoryRelationshipRow[]): void {
    if (this.relationshipsSyncTimer) {
      clearTimeout(this.relationshipsSyncTimer);
      this.relationshipsSyncTimer = null;
    }

    const next = Array.isArray(rows) ? rows.slice() : [];
    this.relationshipsSyncTimer = setTimeout(() => {
      this.relationshipsSyncTimer = null;
      this.relationships = next;
      if (this.selectedGraphNodeKey && !this.relationshipGraphNodes().some((n) => n.key === this.selectedGraphNodeKey)) {
        this.selectedGraphNodeKey = null;
      }
    });
  }

  private tryExtractValueFromMetadata(rel: MemoryRelationshipRow): string | null {
    if (!rel.metadataJson || rel.metadataJson.trim().length === 0) {
      return null;
    }

    try {
      const parsed = JSON.parse(rel.metadataJson) as Record<string, unknown>;
      const candidates = ['value', 'text', 'summary', 'what', 'content', 'excerpt'];
      for (const key of candidates) {
        const value = parsed[key];
        if (typeof value === 'string' && value.trim().length > 0) {
          return value.trim();
        }
      }
    } catch {
      return null;
    }

    return null;
  }

  private async resolveNodeDetail(nodeType: number, nodeId: string): Promise<MemoryNodeDetail | null> {
    if (!this.nodeDetailEndpointAvailable) {
      return null;
    }

    const companionId = this.normalizedCompanionId();
    if (!companionId || !nodeId) {
      return null;
    }

    if (!this.canResolveNodeDetail(nodeType, nodeId)) {
      return null;
    }

    const cacheKey = `${nodeType}:${nodeId}`.toLowerCase();
    const cached = this.nodeDetailCache.get(cacheKey);
    if (cached) {
      return cached;
    }

    try {
      const detail = await this.memoryApi.resolveNodeDetail(this.baseUrl(), companionId, nodeType, nodeId);
      this.nodeDetailCache.set(cacheKey, detail);
      return detail;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        this.nodeDetailEndpointAvailable = false;
      }
      return null;
    }
  }

  private canResolveNodeDetail(nodeType: number, nodeId: string): boolean {
    const trimmed = nodeId.trim();
    if (!trimmed) {
      return false;
    }

    if (nodeType === 0 || nodeType === 1 || nodeType === 2 || nodeType === 4 || nodeType === 5 || nodeType === 6) {
      return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(trimmed);
    }

    return true;
  }

  private composeDetailValue(detail: MemoryNodeDetail | null, fallback: string): string {
    const value = detail?.value?.trim() ?? '';
    const summary = detail?.summary?.trim() ?? '';
    if (value && summary) {
      return `${value}\n\n${summary}`;
    }

    if (value) {
      return value;
    }

    if (summary) {
      return summary;
    }

    return fallback;
  }

  private sortRelationshipRows(a: MemoryRelationshipRow, b: MemoryRelationshipRow): number {
    if (this.relationshipSort === 'confidence') {
      return (Number(b.confidence) || 0) - (Number(a.confidence) || 0);
    }

    if (this.relationshipSort === 'strength') {
      return (Number(b.strength) || 0) - (Number(a.strength) || 0);
    }

    return new Date(b.updatedAtUtc).getTime() - new Date(a.updatedAtUtc).getTime();
  }
}

function isMemoryGraphLike(view: MemoryView): boolean {
  return view === 'graph' || view === 'relationships';
}
