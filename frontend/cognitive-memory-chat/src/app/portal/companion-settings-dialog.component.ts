import { CommonModule } from '@angular/common';
import { Component, Inject, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { CognitiveAtlasComponent } from '../components/cognitive-atlas.component';
import {
  ClientPortalService,
  CompanionCognitiveProfileState,
  CompanionCognitiveProfileVersion,
  CompanionProfile,
} from '../services/client-portal.service';

interface CompanionSettingsDialogData {
  companion: CompanionProfile;
  baseUrl: string;
}

type VerbosityTarget = 'concise' | 'balanced' | 'detailed';
type ReasoningMode = 'heuristic' | 'analytical' | 'hybrid';
type EvolutionMode = 'locked' | 'propose-only' | 'auto-tune';
type ApprovalPolicy = 'human-required' | 'auto-approve-low-risk';

interface CognitiveProfileModel {
  schemaVersion: string;
  attention: {
    focusStickiness: number;
    explorationBreadth: number;
    clarificationFrequency: number;
    contextWindowAllocation: {
      working: number;
      episodic: number;
      semantic: number;
      procedural: number;
      self: number;
    };
  };
  memory: {
    retrievalWeights: {
      recency: number;
      semanticMatch: number;
      evidenceStrength: number;
      relationshipDegree: number;
      confidence: number;
    };
    layerPriorities: {
      working: number;
      episodic: number;
      semantic: number;
      procedural: number;
      self: number;
      identityBoost: number;
    };
    maxCandidates: number;
    maxResults: number;
    dedupeSensitivity: number;
    writeThresholds: {
      confidenceMin: number;
      importanceMin: number;
    };
    decay: {
      semanticDailyDecay: number;
      episodicDailyDecay: number;
      reinforcementMultiplier: number;
    };
  };
  reasoning: {
    reasoningMode: ReasoningMode;
    structureTemplate: string;
    depth: number;
    evidenceStrictness: number;
  };
  expression: {
    verbosityTarget: VerbosityTarget;
    toneStyle: string;
    emotionalExpressivity: number;
    formatRigidity: number;
  };
  reflection: {
    selfCritiqueEnabled: boolean;
    selfCritiqueRate: number;
    maxSelfCritiquePasses: number;
    debate: {
      triggerSensitivity: number;
      turnCap: number;
      terminationConfidenceThreshold: number;
      convergenceDeltaMin: number;
    };
  };
  uncertainty: {
    answerConfidenceThreshold: number;
    clarifyConfidenceThreshold: number;
    deferConfidenceThreshold: number;
    conflictEscalationThreshold: number;
    requireCitationsInHighRiskDomains: boolean;
  };
  adaptation: {
    procedurality: number;
    adaptivity: number;
    policyStrictness: number;
  };
  evolution: {
    evolutionMode: EvolutionMode;
    maxDailyDelta: number;
    learningSignals: {
      userSatisfaction: boolean;
      hallucinationDetections: boolean;
      clarificationRate: boolean;
      latencyBreaches: boolean;
    };
    approvalPolicy: ApprovalPolicy;
  };
}

@Component({
  selector: 'app-companion-settings-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatTabsModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    CognitiveAtlasComponent,
  ],
  template: `
    <h2 mat-dialog-title class="settings-title">{{ data.companion.name }} Settings</h2>

    <mat-dialog-content class="settings-content">
      <mat-tab-group class="settings-tabs" dynamicHeight>
        <mat-tab label="General">
          <section class="tab-grid">
            <article class="panel-block">
              <h3>Identity</h3>
              <dl>
                <div><dt>Name</dt><dd>{{ data.companion.name }}</dd></div>
                <div><dt>Tone</dt><dd>{{ data.companion.tone }}</dd></div>
                <div><dt>Purpose</dt><dd>{{ data.companion.purpose }}</dd></div>
                <div><dt>Model</dt><dd>{{ data.companion.modelHint }}</dd></div>
                <div><dt>Session</dt><dd>{{ data.companion.sessionId }}</dd></div>
              </dl>
            </article>

            <article class="panel-block">
              <h3>Lifecycle</h3>
              <dl>
                <div><dt>Created</dt><dd>{{ data.companion.createdAtUtc | date: 'medium' }}</dd></div>
                <div><dt>Updated</dt><dd>{{ data.companion.updatedAtUtc | date: 'medium' }}</dd></div>
                <div><dt>Birth (UTC)</dt><dd>{{ data.companion.birthDateUtc ? (data.companion.birthDateUtc | date: 'medium') : 'Not set' }}</dd></div>
                <div><dt>Status</dt><dd>{{ data.companion.isArchived ? 'Archived' : 'Active' }}</dd></div>
              </dl>
            </article>
          </section>

          <article class="panel-block" *ngIf="data.companion.originStory">
            <h3>Origin Story</h3>
            <p class="body-copy">{{ data.companion.originStory }}</p>
          </article>
        </mat-tab>

        <mat-tab label="Cognitive Control">
          <section class="control-stack">
            <article class="panel-block">
              <div class="panel-top">
                <h3>Cognitive Editor</h3>
                <span *ngIf="state() as profileState" class="state-pill">active: v{{ profileState.activeVersionNumber }} · {{ profileState.validationStatus }}</span>
              </div>

              <p class="hint">Use presets to start quickly, then tune each control with plain-language inputs.</p>
              <p class="hint" *ngIf="loading()">Loading cognitive profile...</p>
              <p class="ok" *ngIf="!loading() && message()">{{ message() }}</p>
              <p class="err" *ngIf="!loading() && error()">{{ error() }}</p>

              <div class="preset-row">
                <button mat-stroked-button type="button" (click)="applyPreset('friendly')">Friendly</button>
                <button mat-stroked-button type="button" (click)="applyPreset('analyst')">Analyst</button>
                <button mat-stroked-button type="button" (click)="applyPreset('coach')">Coach</button>
                <button mat-stroked-button type="button" (click)="applyPreset('balanced')">Balanced</button>
              </div>
            </article>

            <app-cognitive-atlas
              [profile]="profile"
              title="Companion Brain Map"
              subtitle="Click a region to jump to controls"
              (regionSelected)="focusRegion($event)"></app-cognitive-atlas>

            <section class="editor-grid">
              <article class="panel-block control-card" id="region-attention">
                <h4>Attention + Focus</h4>
                <div class="slider-row">
                  <label>Focus Stickiness <span>{{ profile.attention.focusStickiness | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.attention.focusStickiness" />
                </div>
                <mat-form-field appearance="outline">
                  <mat-label>Exploration Breadth</mat-label>
                  <input matInput type="number" min="1" max="6" step="1" [(ngModel)]="profile.attention.explorationBreadth" />
                </mat-form-field>
                <div class="slider-row">
                  <label>Clarification Frequency <span>{{ profile.attention.clarificationFrequency | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.attention.clarificationFrequency" />
                </div>
              </article>

              <article class="panel-block control-card" id="region-reasoning">
                <h4>Reasoning</h4>
                <mat-form-field appearance="outline">
                  <mat-label>Reasoning Mode</mat-label>
                  <mat-select [(ngModel)]="profile.reasoning.reasoningMode">
                    <mat-option value="heuristic">Heuristic</mat-option>
                    <mat-option value="analytical">Analytical</mat-option>
                    <mat-option value="hybrid">Hybrid</mat-option>
                  </mat-select>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Structure Template</mat-label>
                  <input matInput [(ngModel)]="profile.reasoning.structureTemplate" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Reasoning Depth</mat-label>
                  <input matInput type="number" min="1" max="4" step="1" [(ngModel)]="profile.reasoning.depth" />
                </mat-form-field>
                <div class="slider-row">
                  <label>Evidence Strictness <span>{{ profile.reasoning.evidenceStrictness | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.reasoning.evidenceStrictness" />
                </div>
              </article>

              <article class="panel-block control-card" id="region-expression">
                <h4>Expression + Tone</h4>
                <mat-form-field appearance="outline">
                  <mat-label>Verbosity Target</mat-label>
                  <mat-select [(ngModel)]="profile.expression.verbosityTarget">
                    <mat-option value="concise">Concise</mat-option>
                    <mat-option value="balanced">Balanced</mat-option>
                    <mat-option value="detailed">Detailed</mat-option>
                  </mat-select>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Tone Style</mat-label>
                  <input matInput [(ngModel)]="profile.expression.toneStyle" />
                </mat-form-field>
                <div class="slider-row">
                  <label>Emotional Expressivity <span>{{ profile.expression.emotionalExpressivity | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.expression.emotionalExpressivity" />
                </div>
                <div class="slider-row">
                  <label>Format Rigidity <span>{{ profile.expression.formatRigidity | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.expression.formatRigidity" />
                </div>
              </article>

              <article class="panel-block control-card" id="region-reflection">
                <h4>Reflection + Debate</h4>
                <mat-slide-toggle [(ngModel)]="profile.reflection.selfCritiqueEnabled">Enable Self-Critique</mat-slide-toggle>
                <div class="slider-row">
                  <label>Self-Critique Rate <span>{{ profile.reflection.selfCritiqueRate | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.reflection.selfCritiqueRate" />
                </div>
                <mat-form-field appearance="outline">
                  <mat-label>Max Critique Passes</mat-label>
                  <input matInput type="number" min="0" max="3" step="1" [(ngModel)]="profile.reflection.maxSelfCritiquePasses" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Debate Turn Cap</mat-label>
                  <input matInput type="number" min="1" max="20" step="1" [(ngModel)]="profile.reflection.debate.turnCap" />
                </mat-form-field>
                <div class="slider-row">
                  <label>Debate Trigger Sensitivity <span>{{ profile.reflection.debate.triggerSensitivity | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.reflection.debate.triggerSensitivity" />
                </div>
              </article>

              <article class="panel-block control-card" id="region-uncertainty">
                <h4>Uncertainty + Confidence</h4>
                <div class="slider-row">
                  <label>Answer Threshold <span>{{ profile.uncertainty.answerConfidenceThreshold | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.uncertainty.answerConfidenceThreshold" />
                </div>
                <div class="slider-row">
                  <label>Clarify Threshold <span>{{ profile.uncertainty.clarifyConfidenceThreshold | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.uncertainty.clarifyConfidenceThreshold" />
                </div>
                <div class="slider-row">
                  <label>Defer Threshold <span>{{ profile.uncertainty.deferConfidenceThreshold | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.uncertainty.deferConfidenceThreshold" />
                </div>
                <mat-slide-toggle [(ngModel)]="profile.uncertainty.requireCitationsInHighRiskDomains">Require citations in high-risk domains</mat-slide-toggle>
              </article>

              <article class="panel-block control-card" id="region-memory">
                <h4>Memory Behavior</h4>
                <mat-form-field appearance="outline">
                  <mat-label>Max Candidates</mat-label>
                  <input matInput type="number" min="10" max="500" step="5" [(ngModel)]="profile.memory.maxCandidates" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Max Results</mat-label>
                  <input matInput type="number" min="1" max="50" step="1" [(ngModel)]="profile.memory.maxResults" />
                </mat-form-field>
                <div class="slider-row">
                  <label>Recency Weight <span>{{ profile.memory.retrievalWeights.recency | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1.5" step="0.01" [(ngModel)]="profile.memory.retrievalWeights.recency" />
                </div>
                <div class="slider-row">
                  <label>Semantic Match Weight <span>{{ profile.memory.retrievalWeights.semanticMatch | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1.5" step="0.01" [(ngModel)]="profile.memory.retrievalWeights.semanticMatch" />
                </div>
                <div class="slider-row">
                  <label>Write Confidence Min <span>{{ profile.memory.writeThresholds.confidenceMin | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.memory.writeThresholds.confidenceMin" />
                </div>
                <div class="slider-row">
                  <label>Write Importance Min <span>{{ profile.memory.writeThresholds.importanceMin | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.memory.writeThresholds.importanceMin" />
                </div>
              </article>

              <article class="panel-block control-card" id="region-adaptation">
                <h4>Adaptation + Evolution</h4>
                <div class="slider-row">
                  <label>Procedurality <span>{{ profile.adaptation.procedurality | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.adaptation.procedurality" />
                </div>
                <div class="slider-row">
                  <label>Adaptivity <span>{{ profile.adaptation.adaptivity | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.adaptation.adaptivity" />
                </div>
                <div class="slider-row">
                  <label>Policy Strictness <span>{{ profile.adaptation.policyStrictness | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.adaptation.policyStrictness" />
                </div>
                <mat-form-field appearance="outline">
                  <mat-label>Evolution Mode</mat-label>
                  <mat-select [(ngModel)]="profile.evolution.evolutionMode">
                    <mat-option value="locked">Locked</mat-option>
                    <mat-option value="propose-only">Propose only</mat-option>
                    <mat-option value="auto-tune">Auto tune</mat-option>
                  </mat-select>
                </mat-form-field>
                <div class="slider-row">
                  <label>Max Daily Delta <span>{{ profile.evolution.maxDailyDelta | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="0.2" step="0.01" [(ngModel)]="profile.evolution.maxDailyDelta" />
                </div>
              </article>
            </section>

            <article class="panel-block">
              <details>
                <summary>Advanced JSON (Expert)</summary>
                <p class="hint">You can still import/export raw JSON when needed.</p>
                <textarea class="draft-input" [(ngModel)]="advancedJsonText"></textarea>
                <div class="row actions-row">
                  <button mat-stroked-button type="button" (click)="refreshAdvancedJsonFromEditor()">Export From Editor</button>
                  <button mat-stroked-button type="button" (click)="applyAdvancedJson()">Import Into Editor</button>
                </div>
              </details>
            </article>

            <article class="panel-block">
              <div class="row actions-row">
                <button mat-stroked-button type="button" (click)="validateDraft()" [disabled]="loading()">Validate</button>
                <button mat-raised-button color="primary" type="button" (click)="saveVersion()" [disabled]="loading()">Save Version</button>
                <button mat-raised-button color="primary" type="button" (click)="activateVersion()" [disabled]="loading()">Activate</button>
                <button mat-stroked-button type="button" (click)="rollbackVersion()" [disabled]="loading()">Rollback</button>
              </div>
            </article>

            <article class="panel-block">
              <h3>Versions</h3>
              <div class="version-list" *ngIf="versions().length > 0; else noVersions">
                <button
                  class="version-item"
                  type="button"
                  *ngFor="let version of versions()"
                  [class.active]="selectedVersionId() === version.profileVersionId"
                  (click)="selectVersion(version)">
                  <div class="version-top">
                    <strong>v{{ version.versionNumber }}</strong>
                    <span>{{ version.validationStatus }}</span>
                  </div>
                  <p>{{ version.changeSummary || version.changeReason || 'No summary' }}</p>
                  <small>{{ version.createdAtUtc | date: 'medium' }}</small>
                </button>
              </div>

              <ng-template #noVersions>
                <p class="hint">No versions available yet.</p>
              </ng-template>
            </article>
          </section>
        </mat-tab>

        <mat-tab label="Version History">
          <article class="panel-block">
            <h3>Audit Trail Snapshot</h3>
            <p class="hint">Recent versions are immutable. Activate any previous version to rollback behavior safely.</p>
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Version</th>
                    <th>Status</th>
                    <th>Created</th>
                    <th>Summary</th>
                  </tr>
                </thead>
                <tbody>
                  <tr *ngFor="let version of versions()">
                    <td>v{{ version.versionNumber }}</td>
                    <td>{{ version.validationStatus }}</td>
                    <td>{{ version.createdAtUtc | date: 'short' }}</td>
                    <td>{{ version.changeSummary || version.changeReason || '-' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </article>
        </mat-tab>
      </mat-tab-group>
    </mat-dialog-content>

    <mat-dialog-actions align="end" class="settings-actions">
      <button mat-stroked-button type="button" (click)="close()">Close</button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .settings-title {
        padding-bottom: 0.25rem;
      }

      .settings-content {
        max-height: min(82vh, 980px);
        overflow: auto;
      }

      .settings-tabs {
        margin-top: 0.2rem;
      }

      .tab-grid {
        display: grid;
        gap: 0.8rem;
        grid-template-columns: minmax(0, 1.35fr) minmax(260px, 1fr);
        padding-top: 0.8rem;
      }

      .control-stack {
        display: grid;
        gap: 0.8rem;
        padding-top: 0.8rem;
      }

      .editor-grid {
        display: grid;
        gap: 0.72rem;
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }

      .control-card h4 {
        margin: 0 0 0.6rem;
        font-size: 0.9rem;
      }

      .panel-block {
        border: 1px solid rgba(113, 148, 198, 0.25);
        border-radius: 0.9rem;
        padding: 0.9rem;
        background: rgba(10, 18, 32, 0.72);
      }

      .panel-top {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 0.6rem;
        margin-bottom: 0.55rem;
      }

      .state-pill {
        font-size: 0.72rem;
        border-radius: 999px;
        padding: 0.2rem 0.5rem;
        border: 1px solid rgba(123, 170, 236, 0.3);
      }

      h3 {
        font-size: 1rem;
        margin: 0;
      }

      h4 {
        font-size: 0.92rem;
      }

      dl {
        display: grid;
        gap: 0.46rem;
      }

      dt {
        font-size: 0.7rem;
        text-transform: uppercase;
        letter-spacing: 0.14em;
        opacity: 0.78;
      }

      dd {
        margin: 0.14rem 0 0;
        font-size: 0.92rem;
        word-break: break-word;
      }

      .body-copy {
        margin: 0.5rem 0 0;
        line-height: 1.45;
      }

      .hint,
      .ok,
      .err {
        margin: 0.24rem 0 0.5rem;
        font-size: 0.86rem;
      }

      .ok {
        color: #a8f5ce;
      }

      .err {
        color: #ffc1d0;
      }

      .preset-row {
        display: flex;
        flex-wrap: wrap;
        gap: 0.46rem;
      }

      .slider-row {
        display: grid;
        gap: 0.3rem;
        margin-bottom: 0.6rem;
      }

      .slider-row label {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 0.5rem;
        font-size: 0.8rem;
        opacity: 0.88;
      }

      .slider-row input[type='range'] {
        width: 100%;
      }

      .draft-input {
        min-height: 220px;
        width: 100%;
        font-family: 'IBM Plex Mono', ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
        font-size: 0.78rem;
        border-radius: 0.75rem;
      }

      .actions-row {
        margin-top: 0.55rem;
        gap: 0.45rem;
      }

      .version-list {
        display: grid;
        gap: 0.46rem;
        max-height: 500px;
        overflow: auto;
      }

      .version-item {
        width: 100%;
        text-align: left;
        border: 1px solid rgba(113, 148, 198, 0.25);
        border-radius: 0.75rem;
        padding: 0.6rem;
        background: rgba(9, 14, 24, 0.64);
      }

      .version-item.active {
        border-color: rgba(43, 226, 208, 0.72);
        background: rgba(10, 39, 44, 0.82);
      }

      .version-item p {
        margin: 0.28rem 0;
        font-size: 0.78rem;
      }

      .version-item small {
        font-size: 0.7rem;
        opacity: 0.78;
      }

      .version-top {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 0.5rem;
      }

      .settings-actions {
        border-top: 1px solid rgba(113, 148, 198, 0.2);
        margin-top: 0.3rem;
        padding-top: 0.6rem;
      }

      mat-form-field {
        width: 100%;
      }

      @media (max-width: 1120px) {
        .editor-grid {
          grid-template-columns: minmax(0, 1fr);
        }
      }

      @media (max-width: 980px) {
        .tab-grid {
          grid-template-columns: minmax(0, 1fr);
        }
      }
    `,
  ],
})
export class CompanionSettingsDialogComponent implements OnInit {
  private readonly portal = inject(ClientPortalService);

  readonly loading = signal(false);
  readonly state = signal<CompanionCognitiveProfileState | null>(null);
  readonly versions = signal<CompanionCognitiveProfileVersion[]>([]);
  readonly selectedVersionId = signal<string | null>(null);
  readonly message = signal('');
  readonly error = signal('');

  profile: CognitiveProfileModel = this.createDefaultProfile();
  advancedJsonText = '';

  private changed = false;

  constructor(
    @Inject(MAT_DIALOG_DATA) readonly data: CompanionSettingsDialogData,
    private readonly dialogRef: MatDialogRef<CompanionSettingsDialogComponent, boolean>,
  ) {}

  ngOnInit(): void {
    void this.loadProfile();
  }

  close(): void {
    this.dialogRef.close(this.changed);
  }

  applyPreset(preset: 'friendly' | 'analyst' | 'coach' | 'balanced'): void {
    const next = this.createDefaultProfile();

    if (preset === 'analyst') {
      next.reasoning.reasoningMode = 'analytical';
      next.reasoning.depth = 3;
      next.reasoning.evidenceStrictness = 0.86;
      next.expression.verbosityTarget = 'detailed';
      next.expression.emotionalExpressivity = 0.12;
      next.expression.toneStyle = 'analyst';
      next.reflection.selfCritiqueRate = 0.42;
      next.attention.clarificationFrequency = 0.32;
      next.uncertainty.answerConfidenceThreshold = 0.72;
    } else if (preset === 'coach') {
      next.reasoning.reasoningMode = 'hybrid';
      next.reasoning.depth = 2;
      next.expression.verbosityTarget = 'balanced';
      next.expression.emotionalExpressivity = 0.56;
      next.expression.toneStyle = 'coach';
      next.reflection.selfCritiqueRate = 0.2;
      next.attention.clarificationFrequency = 0.24;
      next.adaptation.adaptivity = 0.58;
      next.adaptation.procedurality = 0.44;
    } else if (preset === 'friendly') {
      next.reasoning.reasoningMode = 'hybrid';
      next.reasoning.depth = 2;
      next.expression.verbosityTarget = 'balanced';
      next.expression.emotionalExpressivity = 0.34;
      next.expression.toneStyle = 'friendly';
      next.reflection.selfCritiqueRate = 0.22;
    }

    this.profile = next;
    this.refreshAdvancedJsonFromEditor();
    this.message.set(`Applied ${preset} preset.`);
    this.error.set('');
  }

  async validateDraft(): Promise<void> {
    this.message.set('');
    this.error.set('');

    const profile = this.sanitizeProfile(this.profile);
    const result = await this.portal.validateCognitiveProfile(this.data.baseUrl, this.data.companion.companionId, profile);
    if (!result.isValid) {
      this.error.set(`Validation failed: ${result.errors.join(' | ')}`);
      return;
    }

    this.message.set(result.warnings.length > 0 ? `Validated with warnings: ${result.warnings.join(' | ')}` : 'Profile validated.');
    if (result.normalizedProfile) {
      this.profile = this.normalizeProfile(result.normalizedProfile);
      this.refreshAdvancedJsonFromEditor();
    }
  }

  async saveVersion(): Promise<void> {
    this.message.set('');
    this.error.set('');

    const created = await this.portal.createCognitiveProfileVersion(
      this.data.baseUrl,
      this.data.companion.companionId,
      this.sanitizeProfile(this.profile),
      'Settings update',
      'User tuned cognitive controls',
      false,
    );

    this.changed = true;
    this.message.set(`Created version v${created.versionNumber}.`);
    await this.loadProfile(created.profileVersionId);
  }

  async activateVersion(profileVersionId?: string): Promise<void> {
    const targetVersionId = profileVersionId ?? this.selectedVersionId();
    if (!targetVersionId) {
      this.error.set('Select a profile version to activate.');
      return;
    }

    this.error.set('');
    await this.portal.activateCognitiveProfile(this.data.baseUrl, this.data.companion.companionId, targetVersionId, 'Settings activation');
    this.changed = true;
    this.message.set('Activated selected cognitive profile.');
    await this.loadProfile(targetVersionId);
  }

  async rollbackVersion(profileVersionId?: string): Promise<void> {
    const targetVersionId = profileVersionId ?? this.selectedVersionId();
    if (!targetVersionId) {
      this.error.set('Select a profile version to rollback to.');
      return;
    }

    this.error.set('');
    await this.portal.rollbackCognitiveProfile(this.data.baseUrl, this.data.companion.companionId, targetVersionId, 'Settings rollback');
    this.changed = true;
    this.message.set('Rollback completed.');
    await this.loadProfile(targetVersionId);
  }

  selectVersion(version: CompanionCognitiveProfileVersion): void {
    this.selectedVersionId.set(version.profileVersionId);
    this.profile = this.normalizeProfile(this.tryParseJson(version.profileJson));
    this.refreshAdvancedJsonFromEditor();
    this.message.set(`Loaded version v${version.versionNumber}.`);
    this.error.set('');
  }

  refreshAdvancedJsonFromEditor(): void {
    this.advancedJsonText = JSON.stringify(this.sanitizeProfile(this.profile), null, 2);
  }

  applyAdvancedJson(): void {
    const parsed = this.tryParseJson(this.advancedJsonText);
    if (!parsed) {
      this.error.set('Advanced JSON is invalid.');
      return;
    }

    this.profile = this.normalizeProfile(parsed);
    this.message.set('Imported advanced JSON into editor.');
    this.error.set('');
  }

  focusRegion(region: string): void {
    const target = region === 'attention'
      ? 'region-attention'
      : region === 'memory'
        ? 'region-memory'
        : region === 'reasoning'
          ? 'region-reasoning'
          : region === 'reflection'
            ? 'region-reflection'
            : region === 'expression'
              ? 'region-expression'
              : region === 'uncertainty'
                ? 'region-uncertainty'
                : 'region-adaptation';

    const el = document.getElementById(target);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
  }

  private async loadProfile(preferredVersionId?: string): Promise<void> {
    this.loading.set(true);
    this.message.set('');
    this.error.set('');
    try {
      const [stateResult, versionsResult] = await Promise.allSettled([
        this.portal.getCognitiveProfile(this.data.baseUrl, this.data.companion.companionId),
        this.portal.listCognitiveProfileVersions(this.data.baseUrl, this.data.companion.companionId, 40),
      ]);

      if (versionsResult.status === 'fulfilled') {
        this.versions.set(versionsResult.value);
      } else {
        this.versions.set([]);
      }

      if (stateResult.status === 'fulfilled') {
        this.state.set(stateResult.value.state);
      } else {
        this.state.set(null);
      }

      const versions = this.versions();
      const activeProfileVersionId = this.state()?.activeProfileVersionId;
      const selected =
        versions.find((x) => x.profileVersionId === preferredVersionId) ??
        versions.find((x) => x.profileVersionId === activeProfileVersionId) ??
        versions[0] ??
        null;

      this.selectedVersionId.set(selected?.profileVersionId ?? null);
      this.profile = this.normalizeProfile(this.tryParseJson(selected?.profileJson));
      this.refreshAdvancedJsonFromEditor();

      if (versions.length === 0) {
        this.error.set('No cognitive profile versions found for this companion yet.');
      }
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Failed to load cognitive settings.');
    } finally {
      this.loading.set(false);
    }
  }

  private tryParseJson(value: string | null | undefined): unknown | null {
    if (!value) {
      return null;
    }

    try {
      return JSON.parse(value);
    } catch {
      return null;
    }
  }

  private createDefaultProfile(): CognitiveProfileModel {
    return {
      schemaVersion: '1.0.0',
      attention: {
        focusStickiness: 0.65,
        explorationBreadth: 2,
        clarificationFrequency: 0.2,
        contextWindowAllocation: { working: 0.34, episodic: 0.2, semantic: 0.24, procedural: 0.12, self: 0.1 },
      },
      memory: {
        retrievalWeights: { recency: 0.8, semanticMatch: 1, evidenceStrength: 0.7, relationshipDegree: 0.45, confidence: 0.65 },
        layerPriorities: { working: 0.2, episodic: 0.4, semantic: 0.6, procedural: 0.45, self: 0.5, identityBoost: 0.9 },
        maxCandidates: 120,
        maxResults: 20,
        dedupeSensitivity: 0.6,
        writeThresholds: { confidenceMin: 0.62, importanceMin: 0.55 },
        decay: { semanticDailyDecay: 0.02, episodicDailyDecay: 0.04, reinforcementMultiplier: 1.2 },
      },
      reasoning: { reasoningMode: 'hybrid', structureTemplate: 'evidence-first', depth: 2, evidenceStrictness: 0.7 },
      expression: { verbosityTarget: 'balanced', toneStyle: this.data?.companion?.tone || 'friendly', emotionalExpressivity: 0.2, formatRigidity: 0.55 },
      reflection: {
        selfCritiqueEnabled: true,
        selfCritiqueRate: 0.25,
        maxSelfCritiquePasses: 1,
        debate: { triggerSensitivity: 0.55, turnCap: 8, terminationConfidenceThreshold: 0.78, convergenceDeltaMin: 0.02 },
      },
      uncertainty: {
        answerConfidenceThreshold: 0.66,
        clarifyConfidenceThreshold: 0.5,
        deferConfidenceThreshold: 0.3,
        conflictEscalationThreshold: 0.74,
        requireCitationsInHighRiskDomains: true,
      },
      adaptation: { procedurality: 0.58, adaptivity: 0.42, policyStrictness: 0.65 },
      evolution: {
        evolutionMode: 'propose-only',
        maxDailyDelta: 0.06,
        learningSignals: { userSatisfaction: true, hallucinationDetections: true, clarificationRate: true, latencyBreaches: true },
        approvalPolicy: 'human-required',
      },
    };
  }

  private normalizeProfile(input: unknown): CognitiveProfileModel {
    const base = this.createDefaultProfile();
    if (!input || typeof input !== 'object') {
      return base;
    }

    const source = input as Partial<CognitiveProfileModel>;

    return {
      ...base,
      schemaVersion: this.stringOr(source.schemaVersion, base.schemaVersion),
      attention: {
        ...base.attention,
        ...(source.attention ?? {}),
        focusStickiness: this.clamp01(source.attention?.focusStickiness, base.attention.focusStickiness),
        explorationBreadth: this.clampInt(source.attention?.explorationBreadth, 1, 6, base.attention.explorationBreadth),
        clarificationFrequency: this.clamp01(source.attention?.clarificationFrequency, base.attention.clarificationFrequency),
        contextWindowAllocation: {
          ...base.attention.contextWindowAllocation,
          ...(source.attention?.contextWindowAllocation ?? {}),
        },
      },
      memory: {
        ...base.memory,
        ...(source.memory ?? {}),
        retrievalWeights: { ...base.memory.retrievalWeights, ...(source.memory?.retrievalWeights ?? {}) },
        layerPriorities: { ...base.memory.layerPriorities, ...(source.memory?.layerPriorities ?? {}) },
        maxCandidates: this.clampInt(source.memory?.maxCandidates, 10, 500, base.memory.maxCandidates),
        maxResults: this.clampInt(source.memory?.maxResults, 1, 50, base.memory.maxResults),
        dedupeSensitivity: this.clamp01(source.memory?.dedupeSensitivity, base.memory.dedupeSensitivity),
        writeThresholds: {
          confidenceMin: this.clamp01(source.memory?.writeThresholds?.confidenceMin, base.memory.writeThresholds.confidenceMin),
          importanceMin: this.clamp01(source.memory?.writeThresholds?.importanceMin, base.memory.writeThresholds.importanceMin),
        },
        decay: {
          semanticDailyDecay: this.clamp(source.memory?.decay?.semanticDailyDecay, 0, 0.2, base.memory.decay.semanticDailyDecay),
          episodicDailyDecay: this.clamp(source.memory?.decay?.episodicDailyDecay, 0, 0.2, base.memory.decay.episodicDailyDecay),
          reinforcementMultiplier: this.clamp(source.memory?.decay?.reinforcementMultiplier, 0.5, 2.5, base.memory.decay.reinforcementMultiplier),
        },
      },
      reasoning: {
        ...base.reasoning,
        ...(source.reasoning ?? {}),
        reasoningMode: this.asReasoningMode(source.reasoning?.reasoningMode, base.reasoning.reasoningMode),
        structureTemplate: this.stringOr(source.reasoning?.structureTemplate, base.reasoning.structureTemplate),
        depth: this.clampInt(source.reasoning?.depth, 1, 4, base.reasoning.depth),
        evidenceStrictness: this.clamp01(source.reasoning?.evidenceStrictness, base.reasoning.evidenceStrictness),
      },
      expression: {
        ...base.expression,
        ...(source.expression ?? {}),
        verbosityTarget: this.asVerbosity(source.expression?.verbosityTarget, base.expression.verbosityTarget),
        toneStyle: this.stringOr(source.expression?.toneStyle, base.expression.toneStyle),
        emotionalExpressivity: this.clamp01(source.expression?.emotionalExpressivity, base.expression.emotionalExpressivity),
        formatRigidity: this.clamp01(source.expression?.formatRigidity, base.expression.formatRigidity),
      },
      reflection: {
        ...base.reflection,
        ...(source.reflection ?? {}),
        selfCritiqueEnabled: this.boolOr(source.reflection?.selfCritiqueEnabled, base.reflection.selfCritiqueEnabled),
        selfCritiqueRate: this.clamp01(source.reflection?.selfCritiqueRate, base.reflection.selfCritiqueRate),
        maxSelfCritiquePasses: this.clampInt(source.reflection?.maxSelfCritiquePasses, 0, 3, base.reflection.maxSelfCritiquePasses),
        debate: {
          ...base.reflection.debate,
          ...(source.reflection?.debate ?? {}),
          triggerSensitivity: this.clamp01(source.reflection?.debate?.triggerSensitivity, base.reflection.debate.triggerSensitivity),
          turnCap: this.clampInt(source.reflection?.debate?.turnCap, 1, 20, base.reflection.debate.turnCap),
          terminationConfidenceThreshold: this.clamp01(source.reflection?.debate?.terminationConfidenceThreshold, base.reflection.debate.terminationConfidenceThreshold),
          convergenceDeltaMin: this.clamp(source.reflection?.debate?.convergenceDeltaMin, 0, 0.2, base.reflection.debate.convergenceDeltaMin),
        },
      },
      uncertainty: {
        ...base.uncertainty,
        ...(source.uncertainty ?? {}),
        answerConfidenceThreshold: this.clamp01(source.uncertainty?.answerConfidenceThreshold, base.uncertainty.answerConfidenceThreshold),
        clarifyConfidenceThreshold: this.clamp01(source.uncertainty?.clarifyConfidenceThreshold, base.uncertainty.clarifyConfidenceThreshold),
        deferConfidenceThreshold: this.clamp01(source.uncertainty?.deferConfidenceThreshold, base.uncertainty.deferConfidenceThreshold),
        conflictEscalationThreshold: this.clamp01(source.uncertainty?.conflictEscalationThreshold, base.uncertainty.conflictEscalationThreshold),
        requireCitationsInHighRiskDomains: this.boolOr(
          source.uncertainty?.requireCitationsInHighRiskDomains,
          base.uncertainty.requireCitationsInHighRiskDomains,
        ),
      },
      adaptation: {
        ...base.adaptation,
        ...(source.adaptation ?? {}),
        procedurality: this.clamp01(source.adaptation?.procedurality, base.adaptation.procedurality),
        adaptivity: this.clamp01(source.adaptation?.adaptivity, base.adaptation.adaptivity),
        policyStrictness: this.clamp01(source.adaptation?.policyStrictness, base.adaptation.policyStrictness),
      },
      evolution: {
        ...base.evolution,
        ...(source.evolution ?? {}),
        evolutionMode: this.asEvolutionMode(source.evolution?.evolutionMode, base.evolution.evolutionMode),
        maxDailyDelta: this.clamp(source.evolution?.maxDailyDelta, 0, 0.2, base.evolution.maxDailyDelta),
        learningSignals: {
          ...base.evolution.learningSignals,
          ...(source.evolution?.learningSignals ?? {}),
        },
        approvalPolicy: this.asApprovalPolicy(source.evolution?.approvalPolicy, base.evolution.approvalPolicy),
      },
    };
  }

  private sanitizeProfile(input: CognitiveProfileModel): CognitiveProfileModel {
    return this.normalizeProfile(input);
  }

  private clamp(value: unknown, min: number, max: number, fallback: number): number {
    const n = Number(value);
    if (!Number.isFinite(n)) {
      return fallback;
    }

    return Math.max(min, Math.min(max, n));
  }

  private clamp01(value: unknown, fallback: number): number {
    return this.clamp(value, 0, 1, fallback);
  }

  private clampInt(value: unknown, min: number, max: number, fallback: number): number {
    const n = Math.round(Number(value));
    if (!Number.isFinite(n)) {
      return fallback;
    }

    return Math.max(min, Math.min(max, n));
  }

  private stringOr(value: unknown, fallback: string): string {
    return typeof value === 'string' && value.trim().length > 0 ? value : fallback;
  }

  private boolOr(value: unknown, fallback: boolean): boolean {
    return typeof value === 'boolean' ? value : fallback;
  }

  private asVerbosity(value: unknown, fallback: VerbosityTarget): VerbosityTarget {
    return value === 'concise' || value === 'balanced' || value === 'detailed' ? value : fallback;
  }

  private asReasoningMode(value: unknown, fallback: ReasoningMode): ReasoningMode {
    return value === 'heuristic' || value === 'analytical' || value === 'hybrid' ? value : fallback;
  }

  private asEvolutionMode(value: unknown, fallback: EvolutionMode): EvolutionMode {
    return value === 'locked' || value === 'propose-only' || value === 'auto-tune' ? value : fallback;
  }

  private asApprovalPolicy(value: unknown, fallback: ApprovalPolicy): ApprovalPolicy {
    return value === 'human-required' || value === 'auto-approve-low-risk' ? value : fallback;
  }
}
