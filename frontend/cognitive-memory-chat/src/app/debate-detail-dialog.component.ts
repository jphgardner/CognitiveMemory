import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { marked } from 'marked';
import { firstValueFrom } from 'rxjs';
import { DebateEventRow, DebateOutcomeRow, DebateReviewResponse, DebateTurnRow, ScheduledActionRow, SubconsciousDebateRow } from './models/console.models';
import { DebatesStateService } from './services/debates-state.service';

export interface DebateDetailDialogData {
  baseUrl: string;
  companionId: string;
  debateId: string;
  sessionId?: string;
}

@Component({
  selector: 'app-debate-detail-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Debate: {{ detail()?.topicKey ?? data.debateId }}</h2>

    <mat-dialog-content>
      <div class="debate-meta-row">
        <span class="pill">State: {{ detail()?.state ?? 'unknown' }}</span>
        <span class="pill">Resolution: {{ resolutionLabel() }}</span>
        <span class="pill">Trigger: {{ detail()?.triggerEventType ?? 'unknown' }}</span>
        <span class="pill">Turns: {{ turns().length }}</span>
        <span class="pill">Events: {{ events().length }}</span>
      </div>

      <p class="dialog-error" *ngIf="errorText()">{{ errorText() }}</p>

      <section class="required-actions" *ngIf="requiredActions().length > 0">
        <h3>Actions Needed</h3>
        <ul>
          <li *ngFor="let item of requiredActions()">{{ item }}</li>
        </ul>
      </section>

      <section class="decision-box" *ngIf="detail()?.state === 'AwaitingUser'">
        <label>
          <span>Decision notes or guidance</span>
          <textarea [(ngModel)]="decisionInput" rows="4" placeholder="Add guidance for approval/rejection"></textarea>
        </label>
        <label class="checkbox-line">
          <input type="checkbox" [(ngModel)]="queueRerun" />
          <span>Queue a rerun with this input</span>
        </label>
      </section>

      <section class="debate-grid">
        <article class="card">
          <div class="card-head">
            <h3>Conversation</h3>
            <span>{{ turns().length }} turns</span>
          </div>
          <div class="turn-list" *ngIf="turns().length > 0; else noTurns">
            <article class="turn-item" *ngFor="let turn of turns()">
              <div class="turn-top">
                <strong>#{{ turn.turnNumber }} · {{ turn.role }}</strong>
                <span>{{ turn.agentName }} · {{ turn.createdAtUtc | date: 'yyyy-MM-dd HH:mm:ss' }}</span>
              </div>
              <div class="turn-body markdown-body" [innerHTML]="turnHtml(turn)"></div>
            </article>
          </div>
          <ng-template #noTurns>
            <p class="empty">No turn conversation has been captured for this debate yet.</p>
          </ng-template>
        </article>

        <article class="card">
          <div class="card-head">
            <h3>Status + Review</h3>
            <span>{{ reviewStatusLabel() }}</span>
          </div>
          <div class="status-block">
            <p><strong>Created:</strong> {{ detail()?.createdAtUtc | date: 'yyyy-MM-dd HH:mm:ss' }}</p>
            <p><strong>Updated:</strong> {{ detail()?.updatedAtUtc | date: 'yyyy-MM-dd HH:mm:ss' }}</p>
            <p><strong>Outcome:</strong> {{ outcome()?.validationStatus ?? 'none' }} / {{ outcome()?.applyStatus ?? 'none' }}</p>
            <p><strong>Interpretation:</strong> {{ resolutionLabel() }}</p>
            <p *ngIf="detail()?.lastError"><strong>Last error:</strong> {{ detail()?.lastError }}</p>
          </div>
          <div class="decision-box" *ngIf="scheduledFollowUp() as action">
            <p><strong>Deferred Follow-up:</strong> {{ action.status }} · run {{ action.runAtUtc | date: 'yyyy-MM-dd HH:mm:ss' }}</p>
            <p class="event-preview">{{ action.actionType }}</p>
          </div>
          <details>
            <summary>Outcome JSON</summary>
            <pre>{{ prettyOutcome() }}</pre>
          </details>
          <details>
            <summary>Review JSON</summary>
            <pre>{{ prettyReview() }}</pre>
          </details>
        </article>

        <article class="card full-width">
          <div class="card-head">
            <h3>Debate Events</h3>
            <span>{{ events().length }} rows</span>
          </div>
          <div class="event-list" *ngIf="events().length > 0; else noEvents">
            <article class="event-item" *ngFor="let row of events()">
              <div class="event-top">
                <strong>{{ row.eventType }}</strong>
                <span>{{ row.occurredAtUtc | date: 'HH:mm:ss.SSS' }}</span>
              </div>
              <p class="event-meta">{{ row.status }} · retries {{ row.retryCount }}</p>
              <p class="event-preview">{{ row.payloadPreview }}</p>
              <p class="dialog-error" *ngIf="row.lastError">{{ row.lastError }}</p>
            </article>
          </div>
          <ng-template #noEvents>
            <p class="empty">No events recorded for this debate yet.</p>
          </ng-template>
        </article>
      </section>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-stroked-button type="button" (click)="refresh()" [disabled]="busy()">Refresh</button>
      <button mat-stroked-button type="button" (click)="rerun()" [disabled]="busy() || !detail()">Re-run</button>
      <button mat-stroked-button type="button" (click)="decide('reject')" [disabled]="busy() || detail()?.state !== 'AwaitingUser'">Conclude (Skip)</button>
      <button mat-raised-button color="primary" type="button" (click)="decide('approve')" [disabled]="busy() || detail()?.state !== 'AwaitingUser'">Approve</button>
      <button mat-button type="button" (click)="close()">Close</button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      mat-dialog-content {
        min-width: min(1120px, 96vw);
        max-height: 80vh;
      }

      .debate-meta-row {
        display: flex;
        flex-wrap: wrap;
        gap: 0.5rem;
        margin-bottom: 0.75rem;
      }

      .pill {
        border: 1px solid rgba(122, 176, 252, 0.35);
        border-radius: 999px;
        padding: 0.2rem 0.55rem;
        font-size: 0.8rem;
        color: #c5defd;
        background: rgba(72, 120, 198, 0.18);
      }

      .debate-grid {
        display: grid;
        gap: 0.75rem;
        grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr);
      }

      .card {
        border: 1px solid rgba(87, 127, 197, 0.28);
        border-radius: 0.85rem;
        padding: 0.7rem;
        background: rgba(9, 15, 30, 0.72);
      }

      .full-width {
        grid-column: 1 / -1;
      }

      .card-head {
        display: flex;
        justify-content: space-between;
        gap: 0.5rem;
        margin-bottom: 0.5rem;
      }

      .card-head h3 {
        margin: 0;
        font-size: 0.95rem;
      }

      .card-head span {
        color: #9fbad9;
        font-size: 0.78rem;
      }

      .turn-list,
      .event-list {
        display: grid;
        gap: 0.45rem;
      }

      .turn-item,
      .event-item {
        border: 1px solid rgba(99, 138, 206, 0.22);
        border-radius: 0.75rem;
        padding: 0.55rem;
        background: rgba(6, 12, 24, 0.76);
      }

      .turn-top,
      .event-top {
        display: flex;
        justify-content: space-between;
        align-items: baseline;
        gap: 0.5rem;
      }

      .turn-top span,
      .event-top span,
      .event-meta {
        color: #9fbad9;
        font-size: 0.78rem;
      }

      .turn-body {
        margin-top: 0.4rem;
      }

      .decision-box,
      .required-actions {
        border: 1px solid rgba(99, 138, 206, 0.25);
        border-radius: 0.8rem;
        background: rgba(8, 14, 28, 0.74);
        padding: 0.65rem;
        margin-bottom: 0.75rem;
      }

      .required-actions h3 {
        margin: 0 0 0.4rem;
        font-size: 0.92rem;
      }

      .required-actions ul {
        margin: 0;
        padding-left: 1.2rem;
      }

      .checkbox-line {
        display: flex;
        align-items: center;
        gap: 0.5rem;
        margin-top: 0.5rem;
      }

      .status-block p {
        margin: 0 0 0.35rem;
      }

      textarea {
        width: 100%;
        margin-top: 0.3rem;
        border-radius: 0.65rem;
        min-height: 5.25rem;
      }

      pre {
        white-space: pre-wrap;
        overflow-wrap: anywhere;
        background: rgba(4, 9, 19, 0.86);
        border: 1px solid rgba(100, 141, 202, 0.34);
        border-radius: 0.72rem;
        padding: 0.6rem;
      }

      .dialog-error {
        color: #ffc0ca;
      }

      .empty,
      .event-preview {
        color: #9fbad9;
      }

      @media (max-width: 1080px) {
        .debate-grid {
          grid-template-columns: 1fr;
        }

        .full-width {
          grid-column: auto;
        }
      }
    `,
  ],
})
export class DebateDetailDialogComponent implements OnDestroy {
  private readonly debatesApi = inject(DebatesStateService);
  private readonly http = inject(HttpClient);
  private readonly dialogRef = inject(MatDialogRef<DebateDetailDialogComponent>);
  readonly data = inject<DebateDetailDialogData>(MAT_DIALOG_DATA);

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly errorText = signal('');
  readonly detail = signal<SubconsciousDebateRow | null>(null);
  readonly turns = signal<DebateTurnRow[]>([]);
  readonly outcome = signal<DebateOutcomeRow | null>(null);
  readonly review = signal<DebateReviewResponse | null>(null);
  readonly events = signal<DebateEventRow[]>([]);
  readonly scheduledFollowUp = signal<ScheduledActionRow | null>(null);

  decisionInput = '';
  queueRerun = false;

  private readonly markdownCache = new Map<string, string>();
  private refreshTimer: number | null = null;
  private debateEventsEndpointAvailable = true;

  readonly requiredActions = computed(() => {
    const actions: string[] = [];
    const detail = this.detail();
    const review = this.review();
    const outcome = this.outcome();

    if (!detail) {
      return ['Debate details unavailable; refresh and retry.'];
    }

    if (detail.state === 'AwaitingUser') {
      actions.push('User decision required: Approve or Reject this debate.');
    }

    if (review?.validation?.requiresUserInput) {
      actions.push('Validation indicates user input is required before apply.');
    }

    if ((outcome?.applyStatus ?? '').toLowerCase() === 'deferred') {
      actions.push('Debate concluded with deferred apply; inspect scheduled follow-up and keep context stable.');
    }

    if (this.scheduledFollowUp() && detail.state === 'Completed') {
      actions.push('Follow-up job queued; no immediate manual action required unless this repeatedly defers.');
    }

    if (actions.length === 0) {
      actions.push('No immediate manual action required.');
    }

    return actions;
  });

  constructor() {
    void this.load(true);
    this.refreshTimer = window.setInterval(() => {
      if (!this.shouldAutoRefresh()) {
        return;
      }

      void this.load(false);
    }, 2500);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer !== null) {
      window.clearInterval(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  close(): void {
    this.dialogRef.close(true);
  }

  async refresh(): Promise<void> {
    await this.load(true);
  }

  reviewStatusLabel(): string {
    const review = this.review();
    if (!review) {
      return 'no review';
    }

    if (review.validation?.requiresUserInput) {
      return 'user input required';
    }

    return review.validation?.status ?? 'unknown';
  }

  resolutionLabel(): string {
    const detail = this.detail();
    const outcome = this.outcome();
    if (!detail) {
      return 'Unknown';
    }

    const state = (detail.state ?? '').toLowerCase();
    const apply = (outcome?.applyStatus ?? '').toLowerCase();
    if (state === 'awaitinguser') {
      return 'Waiting for user decision';
    }

    if (state === 'completed' && apply === 'deferred') {
      return this.scheduledFollowUp() ? 'Concluded with deferred follow-up queued' : 'Concluded with deferred follow-up';
    }

    if (state === 'completed' && apply === 'applied') {
      return 'Concluded and applied';
    }

    if (state === 'completed' && apply === 'skipped') {
      return 'Concluded and skipped';
    }

    if (state === 'running' || state === 'queued') {
      return 'In progress';
    }

    return detail.state;
  }

  async decide(action: 'approve' | 'reject'): Promise<void> {
    if (this.busy() || this.detail()?.state !== 'AwaitingUser') {
      return;
    }

    this.busy.set(true);
    this.errorText.set('');
    try {
      await this.debatesApi.decide(
        this.data.baseUrl,
        this.data.debateId,
        action,
        this.decisionInput.trim() || null,
        this.queueRerun,
      );
      this.decisionInput = '';
      this.queueRerun = false;
      await this.load(true);
    } catch (error) {
      this.errorText.set(this.toError(error));
    } finally {
      this.busy.set(false);
    }
  }

  async rerun(): Promise<void> {
    const detail = this.detail();
    if (!detail || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.errorText.set('');
    try {
      await this.debatesApi.rerun(this.data.baseUrl, {
        sessionId: detail.sessionId ?? this.data.sessionId ?? '',
        topicKey: detail.topicKey,
        triggerEventType: detail.triggerEventType,
        triggerPayloadJson: detail.triggerPayloadJson ?? '{}',
      });
      await this.load(true);
    } catch (error) {
      this.errorText.set(this.toError(error));
    } finally {
      this.busy.set(false);
    }
  }

  turnHtml(turn: DebateTurnRow): string {
    const text = turn?.message ?? '';
    const cached = this.markdownCache.get(text);
    if (cached !== undefined) {
      return cached;
    }

    const rendered = marked.parse(text, { async: false });
    const html = typeof rendered === 'string' ? rendered : text;
    this.markdownCache.set(text, html);
    if (this.markdownCache.size > 300) {
      const oldest = this.markdownCache.keys().next().value;
      if (oldest) {
        this.markdownCache.delete(oldest);
      }
    }
    return html;
  }

  prettyOutcome(): string {
    const json = this.outcome()?.outcomeJson;
    if (!json) {
      return 'No outcome recorded yet.';
    }

    try {
      return JSON.stringify(JSON.parse(json), null, 2);
    } catch {
      return json;
    }
  }

  prettyReview(): string {
    const review = this.review();
    if (!review) {
      return 'No review data available yet.';
    }

    return JSON.stringify(review, null, 2);
  }

  private shouldAutoRefresh(): boolean {
    const state = (this.detail()?.state ?? '').toLowerCase();
    return state.length === 0 || state === 'running' || state === 'queued' || state === 'awaitinguser';
  }

  private async load(showSpinner: boolean): Promise<void> {
    if (showSpinner) {
      this.loading.set(true);
    }

    try {
      const [detail, turns, outcome, review, eventsResult] = await Promise.all([
        this.debatesApi.detail(this.data.baseUrl, this.data.debateId),
        this.debatesApi.turns(this.data.baseUrl, this.data.debateId),
        this.debatesApi.outcome(this.data.baseUrl, this.data.debateId),
        this.debatesApi.review(this.data.baseUrl, this.data.debateId),
        this.debatesApi.events(this.data.baseUrl, this.data.debateId, this.data.companionId, this.debateEventsEndpointAvailable),
      ]);

      this.detail.set(detail);
      this.turns.set(turns);
      this.outcome.set(outcome);
      this.review.set(review);
      this.debateEventsEndpointAvailable = eventsResult.endpointAvailable;
      this.events.set(eventsResult.rows);
      await this.loadDeferredFollowUpActionAsync(eventsResult.rows);
      this.errorText.set('');
    } catch (error) {
      this.errorText.set(this.toError(error));
    } finally {
      if (showSpinner) {
        this.loading.set(false);
      }
    }
  }

  private toError(error: unknown): string {
    if (error instanceof Error) {
      return error.message;
    }

    try {
      return JSON.stringify(error);
    } catch {
      return String(error);
    }
  }

  private async loadDeferredFollowUpActionAsync(events: DebateEventRow[]): Promise<void> {
    this.scheduledFollowUp.set(null);
    const actionId = this.extractDeferredActionId(events);
    if (!actionId) {
      return;
    }

    try {
      const rows = await firstValueFrom(
        this.http.get<ScheduledActionRow[]>(
          `${this.data.baseUrl}/scheduled-actions?companionId=${encodeURIComponent(this.data.companionId)}&take=250`,
        ),
      );
      this.scheduledFollowUp.set(rows.find((x) => x.actionId.toLowerCase() === actionId.toLowerCase()) ?? null);
    } catch {
      // Best-effort enrichment; debate dialog remains functional without scheduled-action lookup.
    }
  }

  private extractDeferredActionId(events: DebateEventRow[]): string | null {
    const deferred = [...events].find((x) => (x.eventType ?? '').toLowerCase().includes('memoryupdatedeferred'));
    if (!deferred) {
      return null;
    }

    const payload = deferred.payloadPreview ?? '';
    const match = /[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}/i.exec(payload);
    return match?.[0] ?? null;
  }
}
