import { CommonModule } from '@angular/common';
import { Component, Inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { EventingRow, SubconsciousDebateRow } from './models/console.models';

interface WorkspaceInsightsDialogData {
  companionName: string;
  events: EventingRow[];
  claims: Array<{ predicate: string; value: string; confidence: number }>;
  relationships: Array<{ relationshipType: string; fromType: string; fromId: string; toType: string; toId: string }>;
  debates: SubconsciousDebateRow[];
  metrics: {
    toolSuccessRate: number;
    totalToolCalls: number;
    successfulToolCalls: number;
    averagePublishLatencySeconds: number;
    layerDistribution: Array<{ layer: string; count: number; percent: number }>;
  } | null;
}

@Component({
  selector: 'app-workspace-insights-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatTabsModule],
  template: `
    <h2 mat-dialog-title>{{ data.companionName }} Insights</h2>
    <mat-dialog-content class="insights-content">
      <mat-tab-group>
        <mat-tab label="Evidence Feed">
          <section class="pane list-pane">
            <article *ngFor="let row of data.events.slice(0, 240)" class="item">
              <div class="top"><strong>{{ row.eventType }}</strong><span>{{ row.occurredAtUtc | date: 'mediumTime' }}</span></div>
              <p>{{ row.payloadPreview }}</p>
            </article>
            <p *ngIf="data.events.length === 0" class="empty">No evidence events captured yet.</p>
          </section>
        </mat-tab>

        <mat-tab label="Memory">
          <section class="pane split">
            <article>
              <h3>Claims</h3>
              <div class="list-pane">
                <article *ngFor="let claim of data.claims" class="item">
                  <div class="top"><strong>{{ claim.predicate }}</strong><span>{{ claim.confidence | number: '1.2-2' }}</span></div>
                  <p>{{ claim.value }}</p>
                </article>
                <p *ngIf="data.claims.length === 0" class="empty">No claims in packet.</p>
              </div>
            </article>
            <article>
              <h3>Relationships</h3>
              <div class="list-pane">
                <article *ngFor="let rel of data.relationships" class="item">
                  <div class="top"><strong>{{ rel.relationshipType }}</strong></div>
                  <p>{{ rel.fromType }}:{{ rel.fromId }} -> {{ rel.toType }}:{{ rel.toId }}</p>
                </article>
                <p *ngIf="data.relationships.length === 0" class="empty">No relationships in packet.</p>
              </div>
            </article>
          </section>
        </mat-tab>

        <mat-tab label="Debates + Metrics">
          <section class="pane split">
            <article>
              <h3>Debates</h3>
              <div class="list-pane">
                <article *ngFor="let debate of data.debates" class="item">
                  <div class="top"><strong>{{ debate.topicKey }}</strong><span>{{ debate.state }}</span></div>
                  <p>{{ debate.triggerEventType }} · {{ debate.updatedAtUtc | date: 'medium' }}</p>
                </article>
                <p *ngIf="data.debates.length === 0" class="empty">No debates yet.</p>
              </div>
            </article>
            <article *ngIf="data.metrics as metrics">
              <h3>Metrics</h3>
              <div class="metrics-grid">
                <article class="metric"><p>Total Tool Calls</p><h4>{{ metrics.totalToolCalls }}</h4></article>
                <article class="metric"><p>Successful</p><h4>{{ metrics.successfulToolCalls }}</h4></article>
                <article class="metric"><p>Success Rate</p><h4>{{ metrics.toolSuccessRate }}%</h4></article>
                <article class="metric"><p>Avg Latency</p><h4>{{ metrics.averagePublishLatencySeconds }}s</h4></article>
              </div>
              <div class="bars" *ngIf="metrics.layerDistribution.length > 0">
                <div class="bar-row" *ngFor="let row of metrics.layerDistribution">
                  <span>{{ row.layer }}</span>
                  <div class="bar-track"><div class="bar-fill" [style.width.%]="row.percent"></div></div>
                  <strong>{{ row.percent }}%</strong>
                </div>
              </div>
            </article>
          </section>
        </mat-tab>
      </mat-tab-group>
    </mat-dialog-content>

    <mat-dialog-actions align="end" class="actions">
      <button mat-stroked-button mat-dialog-close type="button">Close</button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .insights-content { max-height: min(74vh, 780px); overflow: auto; }
      .pane { padding-top: 0.75rem; }
      .split { display: grid; gap: 0.8rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .list-pane { display: grid; gap: 0.45rem; max-height: 55vh; overflow: auto; }
      .item { border: 1px solid rgba(113, 148, 198, 0.24); border-radius: 0.75rem; padding: 0.56rem; background: rgba(10, 17, 30, 0.72); }
      .top { display: flex; justify-content: space-between; gap: 0.5rem; align-items: baseline; }
      .item p { margin: 0.3rem 0 0; font-size: 0.83rem; }
      .empty { opacity: 0.78; }
      .metrics-grid { display: grid; gap: 0.45rem; grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .metric { border: 1px solid rgba(113, 148, 198, 0.24); border-radius: 0.7rem; padding: 0.55rem; }
      .metric p { margin: 0; font-size: 0.75rem; opacity: 0.8; }
      .metric h4 { margin: 0.18rem 0 0; font-size: 1rem; }
      .bars { margin-top: 0.65rem; display: grid; gap: 0.4rem; }
      .bar-row { display: grid; grid-template-columns: 110px 1fr auto; gap: 0.45rem; align-items: center; }
      .bar-track { height: 8px; border-radius: 999px; background: rgba(95, 129, 177, 0.25); overflow: hidden; }
      .bar-fill { height: 100%; border-radius: inherit; background: linear-gradient(90deg, #18b8d9, #2be7c8); }
      .actions { border-top: 1px solid rgba(113, 148, 198, 0.22); margin-top: 0.35rem; padding-top: 0.6rem; }
      @media (max-width: 920px) { .split { grid-template-columns: minmax(0, 1fr); } }
    `,
  ],
})
export class WorkspaceInsightsDialogComponent {
  constructor(@Inject(MAT_DIALOG_DATA) readonly data: WorkspaceInsightsDialogData) {}
}
