import { CommonModule } from '@angular/common';
import { Component, Inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';

export interface ConsoleControlCenterDialogData {
  apiBaseUrl: string;
  sessionId: string;
  activePage: string;
  statusText: string;
  toolSuccessRate: number;
  averageResponseSeconds: number;
  assistantTurnCount: number;
  liveEventCount: number;
  debateCount: number;
  scheduledCount: number;
}

export interface ConsoleControlCenterDialogResult {
  apiBaseUrl: string;
  sessionId: string;
  action: 'none' | 'refresh_streams' | 'clear_chat';
}

@Component({
  selector: 'app-console-control-center-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatTabsModule],
  template: `
    <h2 mat-dialog-title class="title">Console Control Center</h2>

    <mat-dialog-content class="content">
      <mat-tab-group>
        <mat-tab label="Connection">
          <section class="pane">
            <label>
              <span class="label">API Base</span>
              <input [ngModel]="apiBaseDraft()" (ngModelChange)="apiBaseDraft.set($event)" />
            </label>
            <label>
              <span class="label">Session ID</span>
              <input [ngModel]="sessionIdDraft()" (ngModelChange)="sessionIdDraft.set($event)" />
            </label>
          </section>
        </mat-tab>

        <mat-tab label="Live Status">
          <section class="pane stats-grid">
            <article class="stat"><p class="label">Active Page</p><h3>{{ data.activePage }}</h3></article>
            <article class="stat"><p class="label">Assistant Turns</p><h3>{{ data.assistantTurnCount }}</h3></article>
            <article class="stat"><p class="label">Tool Success</p><h3>{{ data.toolSuccessRate }}%</h3></article>
            <article class="stat"><p class="label">Avg Response</p><h3>{{ data.averageResponseSeconds }}s</h3></article>
            <article class="stat"><p class="label">Event Stream</p><h3>{{ data.liveEventCount }}</h3></article>
            <article class="stat"><p class="label">Debates</p><h3>{{ data.debateCount }}</h3></article>
            <article class="stat"><p class="label">Scheduled</p><h3>{{ data.scheduledCount }}</h3></article>
            <article class="stat full"><p class="label">Status</p><h3>{{ data.statusText }}</h3></article>
          </section>
        </mat-tab>

        <mat-tab label="Actions">
          <section class="pane action-pane">
            <p>Run operational actions without leaving your current view.</p>
            <div class="row">
              <button mat-stroked-button type="button" (click)="setAction('refresh_streams')">Restart Streams</button>
              <button mat-stroked-button type="button" (click)="setAction('clear_chat')">Clear Chat</button>
            </div>
            <p class="current">Selected action: {{ action() === 'none' ? 'none' : action() }}</p>
          </section>
        </mat-tab>
      </mat-tab-group>
    </mat-dialog-content>

    <mat-dialog-actions align="end" class="actions">
      <button mat-stroked-button type="button" (click)="close(false)">Cancel</button>
      <button mat-raised-button color="primary" type="button" (click)="close(true)">Apply</button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .title { padding-bottom: 0.25rem; }
      .content { max-height: min(72vh, 760px); overflow: auto; }
      .pane { display: grid; gap: 0.72rem; padding-top: 0.75rem; }
      .stats-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .stat {
        border: 1px solid rgba(113, 148, 198, 0.26);
        border-radius: 0.8rem;
        padding: 0.65rem;
        background: rgba(10, 17, 30, 0.72);
      }
      .stat h3 { margin: 0.2rem 0 0; font-size: 1rem; }
      .stat.full { grid-column: 1 / -1; }
      .action-pane p { margin: 0; }
      .current { opacity: 0.85; font-size: 0.85rem; }
      .actions { border-top: 1px solid rgba(113, 148, 198, 0.22); margin-top: 0.35rem; padding-top: 0.6rem; }
      @media (max-width: 760px) { .stats-grid { grid-template-columns: minmax(0, 1fr); } }
    `,
  ],
})
export class ConsoleControlCenterDialogComponent {
  readonly apiBaseDraft = signal('');
  readonly sessionIdDraft = signal('');
  readonly action = signal<'none' | 'refresh_streams' | 'clear_chat'>('none');

  constructor(
    @Inject(MAT_DIALOG_DATA) readonly data: ConsoleControlCenterDialogData,
    private readonly dialogRef: MatDialogRef<ConsoleControlCenterDialogComponent, ConsoleControlCenterDialogResult | null>,
  ) {
    this.apiBaseDraft.set(data.apiBaseUrl);
    this.sessionIdDraft.set(data.sessionId);
  }

  setAction(action: 'none' | 'refresh_streams' | 'clear_chat'): void {
    this.action.set(action);
  }

  close(apply: boolean): void {
    if (!apply) {
      this.dialogRef.close(null);
      return;
    }

    this.dialogRef.close({
      apiBaseUrl: this.apiBaseDraft().trim(),
      sessionId: this.sessionIdDraft().trim(),
      action: this.action(),
    });
  }
}
