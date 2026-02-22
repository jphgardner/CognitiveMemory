import { CommonModule } from '@angular/common';
import { Component, Inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export interface WorkspaceMemoryNodeDialogRelationship {
  relationshipType: string;
  peerLabel: string;
  value: string;
  updatedAtUtc: string;
}

export interface WorkspaceMemoryNodeDialogData {
  nodeLabel: string;
  nodeValue: string;
  degree: number;
  relationships: WorkspaceMemoryNodeDialogRelationship[];
}

@Component({
  selector: 'app-workspace-memory-node-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Memory Node Detail</h2>
    <mat-dialog-content>
      <section class="block">
        <p class="label">Selected Node</p>
        <p class="value">{{ data.nodeLabel }}</p>
        <p class="meta">Degree: {{ data.degree }}</p>
      </section>

      <section class="block">
        <p class="label">Value</p>
        <p class="value">{{ data.nodeValue }}</p>
      </section>

      <section class="block">
        <div class="head">
          <p class="label">Connected Relationships</p>
          <span>{{ data.relationships.length }}</span>
        </div>
        <div class="rows" *ngIf="data.relationships.length > 0; else noRows">
          <article class="row-item" *ngFor="let rel of data.relationships">
            <strong>{{ rel.relationshipType }}</strong>
            <p>{{ rel.peerLabel }}</p>
            <p class="meta">{{ rel.value }}</p>
            <span class="meta">{{ rel.updatedAtUtc | date: 'yyyy-MM-dd HH:mm:ss' }}</span>
          </article>
        </div>
        <ng-template #noRows>
          <p class="meta">No connected relationships.</p>
        </ng-template>
      </section>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close type="button">Close</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .block {
      border: 1px solid rgba(148, 163, 184, 0.24);
      border-radius: 0.7rem;
      padding: 0.7rem;
      margin-bottom: 0.75rem;
      background: rgba(15, 23, 42, 0.24);
    }
    .head {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 0.5rem;
    }
    .label {
      margin: 0 0 0.35rem;
      font-size: 0.75rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      opacity: 0.72;
    }
    .value {
      margin: 0;
      word-break: break-word;
      line-height: 1.45;
      white-space: pre-wrap;
    }
    .meta {
      margin: 0.3rem 0 0;
      opacity: 0.8;
      font-size: 0.84rem;
      line-height: 1.35;
      word-break: break-word;
    }
    .rows {
      display: grid;
      gap: 0.55rem;
    }
    .row-item {
      border: 1px solid rgba(148, 163, 184, 0.2);
      border-radius: 0.6rem;
      padding: 0.55rem;
      background: rgba(2, 6, 23, 0.28);
    }
  `],
})
export class WorkspaceMemoryNodeDialogComponent {
  constructor(@Inject(MAT_DIALOG_DATA) public readonly data: WorkspaceMemoryNodeDialogData) {}
}
