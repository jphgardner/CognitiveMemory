import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatStepperModule } from '@angular/material/stepper';
import { CognitiveAtlasComponent } from '../components/cognitive-atlas.component';
import { CreateCompanionPayload } from '../services/client-portal.service';

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
  selector: 'app-create-companion-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatStepperModule,
    MatIconModule,
    CognitiveAtlasComponent,
  ],
  template: `
    <h2 mat-dialog-title class="title">Create AI Companion</h2>

    <mat-dialog-content class="dialog-content">
      <mat-stepper #stepper class="wizard" [linear]="false">
        <mat-step [completed]="templateReady()">
          <ng-template matStepLabel>Template</ng-template>
          <section class="step-body">
            <p class="step-copy">Start from a tuned baseline and customize from there.</p>
            <mat-form-field appearance="outline">
              <mat-label>Template</mat-label>
              <mat-select [(ngModel)]="templateKey" (selectionChange)="onTemplateChange()">
                <mat-option value="friendly">Friendly</mat-option>
                <mat-option value="analyst">Analyst</mat-option>
                <mat-option value="coach">Coach</mat-option>
              </mat-select>
            </mat-form-field>

            <div class="template-preview">
              <h3>{{ templatePreviewTitle() }}</h3>
              <p>{{ templatePreviewBody() }}</p>
            </div>

            <div class="step-actions">
              <button mat-raised-button color="primary" matStepperNext type="button">Continue</button>
            </div>
          </section>
        </mat-step>

        <mat-step [completed]="identityReady()">
          <ng-template matStepLabel>Identity</ng-template>
          <section class="step-body">
            <p class="step-copy">Define how this companion presents itself and what it is for.</p>
            <section class="grid two-col">
              <mat-form-field appearance="outline">
                <mat-label>Name</mat-label>
                <input matInput [(ngModel)]="name" />
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Tone</mat-label>
                <mat-select [(ngModel)]="tone">
                  <mat-option value="friendly">Friendly</mat-option>
                  <mat-option value="professional">Professional</mat-option>
                  <mat-option value="coach">Coach</mat-option>
                  <mat-option value="strategist">Strategist</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" class="full">
                <mat-label>Purpose</mat-label>
                <textarea matInput rows="2" [(ngModel)]="purpose"></textarea>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Model Hint</mat-label>
                <input matInput [(ngModel)]="modelHint" />
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>System Prompt</mat-label>
                <textarea matInput rows="3" [(ngModel)]="systemPrompt"></textarea>
              </mat-form-field>
            </section>

            <div class="step-actions">
              <button mat-stroked-button matStepperPrevious type="button">Back</button>
              <button mat-raised-button color="primary" matStepperNext type="button" [disabled]="!identityReady()">Continue</button>
            </div>
          </section>
        </mat-step>

        <mat-step [completed]="cognitiveReady()">
          <ng-template matStepLabel>Cognitive Control</ng-template>
          <section class="step-body">
            <p class="step-copy">Configure how this companion thinks, recalls, debates, and communicates.</p>

            <article class="template-preview">
              <h3>Quick Presets</h3>
              <div class="preset-row">
                <button mat-stroked-button type="button" (click)="applyCognitivePreset('friendly')">Friendly</button>
                <button mat-stroked-button type="button" (click)="applyCognitivePreset('analyst')">Analyst</button>
                <button mat-stroked-button type="button" (click)="applyCognitivePreset('coach')">Coach</button>
                <button mat-stroked-button type="button" (click)="applyCognitivePreset('balanced')">Balanced</button>
              </div>
            </article>

            <app-cognitive-atlas
              [profile]="profile"
              title="Live Brain Map"
              subtitle="Visual summary of current cognitive settings"
              (regionSelected)="focusRegion($event)"></app-cognitive-atlas>

            <section class="editor-grid">
              <article class="editor-card" id="region-expression">
                <h3>Communication</h3>
                <mat-form-field appearance="outline">
                  <mat-label>Verbosity</mat-label>
                  <mat-select [(ngModel)]="profile.expression.verbosityTarget">
                    <mat-option value="concise">Concise</mat-option>
                    <mat-option value="balanced">Balanced</mat-option>
                    <mat-option value="detailed">Detailed</mat-option>
                  </mat-select>
                </mat-form-field>
                <div class="slider-row">
                  <label>Emotional expressivity <span>{{ profile.expression.emotionalExpressivity | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.expression.emotionalExpressivity" />
                </div>
                <div class="slider-row">
                  <label>Clarification frequency <span>{{ profile.attention.clarificationFrequency | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.attention.clarificationFrequency" />
                </div>
              </article>

              <article class="editor-card" id="region-reasoning">
                <h3>Reasoning</h3>
                <mat-form-field appearance="outline">
                  <mat-label>Reasoning Mode</mat-label>
                  <mat-select [(ngModel)]="profile.reasoning.reasoningMode">
                    <mat-option value="heuristic">Heuristic</mat-option>
                    <mat-option value="hybrid">Hybrid</mat-option>
                    <mat-option value="analytical">Analytical</mat-option>
                  </mat-select>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Structure Style</mat-label>
                  <input matInput [(ngModel)]="profile.reasoning.structureTemplate" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Reasoning Depth</mat-label>
                  <input matInput type="number" min="1" max="4" step="1" [(ngModel)]="profile.reasoning.depth" />
                </mat-form-field>
                <div class="slider-row">
                  <label>Self critique rate <span>{{ profile.reflection.selfCritiqueRate | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.reflection.selfCritiqueRate" />
                </div>
              </article>

              <article class="editor-card" id="region-memory">
                <h3>Memory</h3>
                <mat-form-field appearance="outline">
                  <mat-label>Max retrieval candidates</mat-label>
                  <input matInput type="number" min="10" max="500" step="5" [(ngModel)]="profile.memory.maxCandidates" />
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>Max retrieval results</mat-label>
                  <input matInput type="number" min="1" max="50" step="1" [(ngModel)]="profile.memory.maxResults" />
                </mat-form-field>
                <div class="slider-row">
                  <label>Recency weighting <span>{{ profile.memory.retrievalWeights.recency | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1.5" step="0.01" [(ngModel)]="profile.memory.retrievalWeights.recency" />
                </div>
                <div class="slider-row">
                  <label>Write confidence minimum <span>{{ profile.memory.writeThresholds.confidenceMin | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.memory.writeThresholds.confidenceMin" />
                </div>
              </article>

              <article class="editor-card" id="region-uncertainty">
                <h3>Uncertainty + Evolution</h3>
                <div class="slider-row">
                  <label>Answer confidence threshold <span>{{ profile.uncertainty.answerConfidenceThreshold | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.uncertainty.answerConfidenceThreshold" />
                </div>
                <div class="slider-row">
                  <label>Defer threshold <span>{{ profile.uncertainty.deferConfidenceThreshold | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.uncertainty.deferConfidenceThreshold" />
                </div>
                <mat-form-field appearance="outline">
                  <mat-label>Evolution mode</mat-label>
                  <mat-select [(ngModel)]="profile.evolution.evolutionMode">
                    <mat-option value="locked">Locked</mat-option>
                    <mat-option value="propose-only">Propose only</mat-option>
                    <mat-option value="auto-tune">Auto tune</mat-option>
                  </mat-select>
                </mat-form-field>
                <div class="slider-row">
                  <label>Policy strictness <span>{{ profile.adaptation.policyStrictness | number: '1.2-2' }}</span></label>
                  <input type="range" min="0" max="1" step="0.01" [(ngModel)]="profile.adaptation.policyStrictness" />
                </div>
              </article>
            </section>

            <div class="step-actions">
              <button mat-stroked-button matStepperPrevious type="button">Back</button>
              <button mat-raised-button color="primary" matStepperNext type="button">Continue</button>
            </div>
          </section>
        </mat-step>

        <mat-step>
          <ng-template matStepLabel>Memory + Review</ng-template>
          <section class="step-body">
            <p class="step-copy">Finalize memory seed and confirm everything before creation.</p>

            <section class="grid two-col">
              <mat-form-field appearance="outline" class="full">
                <mat-label>Origin Story</mat-label>
                <textarea matInput rows="3" [(ngModel)]="originStory"></textarea>
              </mat-form-field>

              <mat-form-field appearance="outline">
                <mat-label>Birth Date/Time (UTC)</mat-label>
                <input matInput type="datetime-local" [(ngModel)]="birthDateUtcLocal" />
              </mat-form-field>

              <mat-form-field appearance="outline" class="full">
                <mat-label>Initial Memory Seed</mat-label>
                <textarea matInput rows="3" [(ngModel)]="initialMemoryText"></textarea>
              </mat-form-field>
            </section>

            <article class="review-box">
              <h3>Review</h3>
              <dl>
                <div><dt>Name</dt><dd>{{ name || 'Untitled companion' }}</dd></div>
                <div><dt>Tone</dt><dd>{{ tone }}</dd></div>
                <div><dt>Purpose</dt><dd>{{ purpose }}</dd></div>
                <div><dt>Model</dt><dd>{{ modelHint }}</dd></div>
                <div><dt>Verbosity</dt><dd>{{ profile.expression.verbosityTarget }}</dd></div>
                <div><dt>Reasoning</dt><dd>{{ profile.reasoning.reasoningMode }} / depth {{ profile.reasoning.depth }}</dd></div>
                <div><dt>Confidence Threshold</dt><dd>{{ profile.uncertainty.answerConfidenceThreshold | number: '1.2-2' }}</dd></div>
              </dl>
            </article>

            <div class="step-actions">
              <button mat-stroked-button matStepperPrevious type="button">Back</button>
              <button mat-raised-button color="primary" type="button" (click)="create()" [disabled]="!identityReady()">Create Companion</button>
            </div>
          </section>
        </mat-step>
      </mat-stepper>
    </mat-dialog-content>

    <mat-dialog-actions align="end" class="actions">
      <button mat-stroked-button type="button" (click)="cancel()">Cancel</button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .title {
        padding-bottom: 0.35rem;
      }

      .dialog-content {
        max-height: min(82vh, 980px);
        overflow: auto;
        padding-top: 0.2rem;
      }

      .step-body {
        display: grid;
        gap: 0.7rem;
        padding-top: 0.4rem;
      }

      .step-copy {
        margin: 0;
        font-size: 0.86rem;
        opacity: 0.85;
      }

      .grid {
        display: grid;
        gap: 0.7rem;
      }

      .two-col {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }

      .full {
        grid-column: 1 / -1;
      }

      .template-preview,
      .review-box,
      .editor-card {
        border: 1px solid rgba(113, 148, 198, 0.26);
        border-radius: 0.9rem;
        padding: 0.75rem;
        background: rgba(10, 18, 32, 0.72);
      }

      .template-preview h3,
      .review-box h3,
      .editor-card h3 {
        margin: 0 0 0.3rem;
        font-size: 0.94rem;
      }

      .template-preview p {
        margin: 0;
        font-size: 0.84rem;
        line-height: 1.4;
      }

      .editor-grid {
        display: grid;
        gap: 0.7rem;
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }

      .slider-row {
        display: grid;
        gap: 0.28rem;
        margin-bottom: 0.58rem;
      }

      .slider-row label {
        display: flex;
        justify-content: space-between;
        align-items: center;
        font-size: 0.8rem;
        opacity: 0.88;
      }

      .slider-row input[type='range'] {
        width: 100%;
      }

      .preset-row {
        display: flex;
        flex-wrap: wrap;
        gap: 0.45rem;
      }

      .review-box dl {
        display: grid;
        gap: 0.42rem;
      }

      .review-box dt {
        font-size: 0.68rem;
        letter-spacing: 0.12em;
        text-transform: uppercase;
        opacity: 0.74;
      }

      .review-box dd {
        margin: 0.12rem 0 0;
        font-size: 0.88rem;
      }

      .step-actions {
        display: flex;
        justify-content: flex-end;
        gap: 0.45rem;
        margin-top: 0.15rem;
      }

      .actions {
        margin-top: 0.3rem;
        padding-top: 0.62rem;
        border-top: 1px solid rgba(130, 177, 245, 0.2);
      }

      mat-form-field {
        width: 100%;
      }

      @media (max-width: 980px) {
        .two-col,
        .editor-grid {
          grid-template-columns: minmax(0, 1fr);
        }
      }
    `,
  ],
})
export class CreateCompanionDialogComponent {
  templateKey = 'friendly';
  name = '';
  tone = 'friendly';
  purpose = 'General companion';
  modelHint = 'openai:gpt-4.1-mini';
  originStory = '';
  birthDateUtcLocal = '';
  initialMemoryText = '';
  systemPrompt = 'You are a warm, helpful AI companion. Keep responses concise and practical.';

  profile: CognitiveProfileModel = this.createDefaultProfile();

  constructor(private readonly dialogRef: MatDialogRef<CreateCompanionDialogComponent, CreateCompanionPayload | null>) {}

  cancel(): void {
    this.dialogRef.close(null);
  }

  templateReady(): boolean {
    return this.templateKey.trim().length > 0;
  }

  identityReady(): boolean {
    return this.name.trim().length >= 2 && this.purpose.trim().length >= 3;
  }

  cognitiveReady(): boolean {
    return true;
  }

  templatePreviewTitle(): string {
    if (this.templateKey === 'analyst') {
      return 'Analyst';
    }

    if (this.templateKey === 'coach') {
      return 'Coach';
    }

    return 'Friendly';
  }

  templatePreviewBody(): string {
    if (this.templateKey === 'analyst') {
      return 'Evidence-first, structured responses with explicit uncertainty handling and deeper analysis by default.';
    }

    if (this.templateKey === 'coach') {
      return 'Action-oriented with stronger motivation and habit support while keeping accountability clear.';
    }

    return 'Warm and practical baseline with balanced tone and concise guidance for day-to-day collaboration.';
  }

  onTemplateChange(): void {
    const templates: Record<string, { tone: string; purpose: string; systemPrompt: string; seed: string }> = {
      friendly: {
        tone: 'friendly',
        purpose: 'General companion',
        systemPrompt: 'You are a warm, helpful AI companion. Keep responses concise and practical.',
        seed: 'I support the user with empathetic, clear help and everyday planning.',
      },
      analyst: {
        tone: 'analyst',
        purpose: 'Analysis and decision support',
        systemPrompt: 'You are an analytical AI companion. Prioritize evidence, tradeoffs, and structured reasoning.',
        seed: 'I provide evidence-backed analysis and explicitly call out uncertainty and assumptions.',
      },
      coach: {
        tone: 'coach',
        purpose: 'Growth, habits, and accountability',
        systemPrompt: 'You are a coaching AI companion. Be direct, motivating, and action-oriented.',
        seed: 'I help the user set goals, track progress, and turn plans into consistent habits.',
      },
    };

    const selected = templates[this.templateKey] ?? templates['friendly'];
    this.tone = selected.tone;
    this.purpose = selected.purpose;
    this.systemPrompt = selected.systemPrompt;
    this.initialMemoryText = selected.seed;
    this.applyCognitivePreset(this.templateKey === 'analyst' || this.templateKey === 'coach' ? this.templateKey : 'friendly');
  }

  applyCognitivePreset(preset: 'friendly' | 'analyst' | 'coach' | 'balanced'): void {
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
      next.uncertainty.deferConfidenceThreshold = 0.26;
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
  }

  focusRegion(region: string): void {
    const target = region === 'memory'
      ? 'region-memory'
      : region === 'reasoning' || region === 'reflection'
        ? 'region-reasoning'
        : region === 'attention' || region === 'expression'
          ? 'region-expression'
          : 'region-uncertainty';

    const el = document.getElementById(target);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
  }

  create(): void {
    const payload: CreateCompanionPayload = {
      name: this.name.trim(),
      tone: this.tone.trim() || 'friendly',
      purpose: this.purpose.trim() || 'General companion',
      modelHint: this.modelHint.trim() || 'openai:gpt-4.1-mini',
      originStory: this.originStory.trim(),
      birthDateUtc: this.birthDateUtcLocal ? new Date(this.birthDateUtcLocal).toISOString() : null,
      initialMemoryText: this.initialMemoryText.trim() || null,
      templateKey: this.templateKey,
      systemPrompt: this.systemPrompt.trim() || null,
      metadataJson: null,
      cognitiveProfileJson: JSON.stringify(this.sanitizeProfile(this.profile)),
    };

    this.dialogRef.close(payload);
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
      expression: { verbosityTarget: 'balanced', toneStyle: 'friendly', emotionalExpressivity: 0.2, formatRigidity: 0.55 },
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

  private sanitizeProfile(profile: CognitiveProfileModel): CognitiveProfileModel {
    return {
      ...profile,
      attention: {
        ...profile.attention,
        focusStickiness: this.clamp(profile.attention.focusStickiness, 0, 1),
        explorationBreadth: this.clampInt(profile.attention.explorationBreadth, 1, 6),
        clarificationFrequency: this.clamp(profile.attention.clarificationFrequency, 0, 1),
      },
      memory: {
        ...profile.memory,
        maxCandidates: this.clampInt(profile.memory.maxCandidates, 10, 500),
        maxResults: this.clampInt(profile.memory.maxResults, 1, 50),
        retrievalWeights: {
          ...profile.memory.retrievalWeights,
          recency: this.clamp(profile.memory.retrievalWeights.recency, 0, 1.5),
          semanticMatch: this.clamp(profile.memory.retrievalWeights.semanticMatch, 0, 1.5),
        },
        writeThresholds: {
          confidenceMin: this.clamp(profile.memory.writeThresholds.confidenceMin, 0, 1),
          importanceMin: this.clamp(profile.memory.writeThresholds.importanceMin, 0, 1),
        },
      },
      reasoning: {
        ...profile.reasoning,
        depth: this.clampInt(profile.reasoning.depth, 1, 4),
      },
      expression: {
        ...profile.expression,
        emotionalExpressivity: this.clamp(profile.expression.emotionalExpressivity, 0, 1),
      },
      reflection: {
        ...profile.reflection,
        selfCritiqueRate: this.clamp(profile.reflection.selfCritiqueRate, 0, 1),
      },
      uncertainty: {
        ...profile.uncertainty,
        answerConfidenceThreshold: this.clamp(profile.uncertainty.answerConfidenceThreshold, 0, 1),
        deferConfidenceThreshold: this.clamp(profile.uncertainty.deferConfidenceThreshold, 0, 1),
      },
      adaptation: {
        ...profile.adaptation,
        policyStrictness: this.clamp(profile.adaptation.policyStrictness, 0, 1),
      },
    };
  }

  private clamp(value: number, min: number, max: number): number {
    if (!Number.isFinite(value)) {
      return min;
    }

    return Math.max(min, Math.min(max, value));
  }

  private clampInt(value: number, min: number, max: number): number {
    if (!Number.isFinite(value)) {
      return min;
    }

    const rounded = Math.round(value);
    return Math.max(min, Math.min(max, rounded));
  }
}
