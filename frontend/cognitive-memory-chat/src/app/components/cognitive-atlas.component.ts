import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

type RegionKey = 'attention' | 'memory' | 'reasoning' | 'reflection' | 'expression' | 'uncertainty' | 'adaptation';

interface RegionView {
  key: RegionKey;
  label: string;
  x: number;
  y: number;
  intensity: number;
  subtitle: string;
  detail: string;
  drivers: string[];
}

interface FlowPath {
  from: RegionKey;
  to: RegionKey;
  d: string;
}

interface FlowParticle {
  from: RegionKey;
  to: RegionKey;
  begin: string;
  radius: number;
  color: string;
}

@Component({
  selector: 'app-cognitive-atlas',
  standalone: true,
  imports: [CommonModule],
  template: `
    <article class="atlas-shell" [class.compact]="compact" [class.empty]="!hasProfile()">
      <header class="atlas-header" *ngIf="title || subtitle">
        <div>
          <h3 *ngIf="title">{{ title }}</h3>
          <p *ngIf="subtitle">{{ subtitle }}</p>
        </div>
        <span class="posture-pill">{{ postureText() }}</span>
      </header>

      <div class="atlas-visual" (mouseleave)="hoveredRegion = null">
        <div class="atlas-canvas">
          <svg viewBox="0 0 960 420" role="img" aria-label="Cognitive atlas brain map">
            <defs>
              <radialGradient id="atlasGlow" cx="50%" cy="46%" r="66%">
                <stop offset="0%" stop-color="rgba(96, 231, 255, 0.34)" />
                <stop offset="62%" stop-color="rgba(32, 88, 153, 0.14)" />
                <stop offset="100%" stop-color="rgba(7, 16, 33, 0)" />
              </radialGradient>

              <linearGradient id="brainBase" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stop-color="rgba(28, 52, 92, 0.98)" />
                <stop offset="55%" stop-color="rgba(19, 35, 64, 0.98)" />
                <stop offset="100%" stop-color="rgba(12, 22, 40, 0.99)" />
              </linearGradient>

              <linearGradient id="brainStroke" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" stop-color="rgba(158, 241, 255, 0.8)" />
                <stop offset="100%" stop-color="rgba(250, 96, 190, 0.72)" />
              </linearGradient>

              <radialGradient id="leftLobeTint" cx="32%" cy="46%" r="58%">
                <stop offset="0%" stop-color="rgba(148, 194, 255, 0.12)" />
                <stop offset="100%" stop-color="rgba(81, 124, 194, 0.02)" />
              </radialGradient>

              <radialGradient id="rightLobeTint" cx="68%" cy="46%" r="58%">
                <stop offset="0%" stop-color="rgba(155, 201, 255, 0.11)" />
                <stop offset="100%" stop-color="rgba(85, 128, 198, 0.02)" />
              </radialGradient>

              <filter id="brainShadow" x="-25%" y="-25%" width="150%" height="150%">
                <feDropShadow dx="0" dy="10" stdDeviation="11" flood-color="rgba(0,0,0,0.45)" />
              </filter>

              <filter id="particleGlow">
                <feDropShadow dx="0" dy="0" stdDeviation="2.7" flood-color="rgba(122, 242, 255, 0.85)" />
              </filter>

              <clipPath id="brainClip">
                <path d="M100 228C95 136 158 68 247 46C337 25 429 46 483 104C537 46 629 25 719 46C808 68 871 136 866 228C861 314 806 378 726 394C638 411 548 381 483 322C418 381 328 411 240 394C160 378 105 314 100 228Z" />
              </clipPath>

              <clipPath id="leftClip">
                <path d="M100 228C95 136 158 68 247 46C337 25 429 46 483 104L483 322C418 381 328 411 240 394C160 378 105 314 100 228Z" />
              </clipPath>

              <clipPath id="rightClip">
                <path d="M483 104C537 46 629 25 719 46C808 68 871 136 866 228C861 314 806 378 726 394C638 411 548 381 483 322Z" />
              </clipPath>
            </defs>

            <ellipse cx="483" cy="210" rx="364" ry="166" fill="url(#atlasGlow)"></ellipse>

            <path
              class="brain-mass"
              d="M100 228C95 136 158 68 247 46C337 25 429 46 483 104C537 46 629 25 719 46C808 68 871 136 866 228C861 314 806 378 726 394C638 411 548 381 483 322C418 381 328 411 240 394C160 378 105 314 100 228Z"
              fill="url(#brainBase)"
              stroke="url(#brainStroke)"
              stroke-width="2.35"
              filter="url(#brainShadow)" />

            <path
              class="brain-inner"
              d="M140 229C138 161 186 106 257 89C329 72 401 89 449 126C460 135 471 146 483 158C495 146 506 135 517 126C565 89 637 72 709 89C780 106 828 161 826 229C824 291 780 337 711 354C637 372 558 349 507 305C499 298 491 290 483 281C475 290 467 298 459 305C408 349 329 372 255 354C186 337 142 291 140 229Z" />

            <path class="left-lobe-tint" d="M115 166C151 95 225 58 310 56C372 56 427 76 473 111C402 116 343 140 294 184C249 224 217 274 197 334C148 316 122 274 115 166Z" />
            <path class="right-lobe-tint" d="M851 166C815 95 741 58 656 56C594 56 539 76 493 111C564 116 623 140 672 184C717 224 749 274 769 334C818 316 844 274 851 166Z" />

            <path class="brainstem" d="M456 323C468 355 489 376 517 384C551 394 592 384 622 360C584 349 548 331 514 308C496 315 476 320 456 323Z" />

            <path class="midline-fissure" d="M483 101C473 147 471 201 475 252C478 285 485 313 497 338" />
            <path class="midline-fissure" d="M483 101C493 147 495 201 491 252C488 285 481 313 469 338" />

            <g clip-path="url(#leftClip)">
              <path class="sulcus major" d="M150 120C223 76 307 68 381 88C421 99 451 118 474 146" />
              <path class="sulcus major" d="M136 168C218 130 314 121 394 140C438 149 468 168 488 195" />
              <path class="sulcus major" d="M154 222C236 190 321 184 397 201C443 210 474 228 493 251" />
              <path class="sulcus major" d="M198 279C275 249 348 243 407 257C452 266 483 281 500 299" />

              <path class="sulcus minor" d="M180 99C241 72 306 66 366 76C405 83 436 95 462 113" />
              <path class="sulcus minor" d="M163 139C238 108 313 101 379 112C421 118 454 130 479 147" />
              <path class="sulcus minor" d="M153 188C234 161 315 153 385 162C428 167 463 177 488 193" />
              <path class="sulcus minor" d="M156 241C237 217 315 209 385 217C430 222 466 231 492 247" />
              <path class="sulcus minor" d="M200 297C269 275 333 268 390 274C430 279 466 287 493 300" />
              <path class="sulcus minor" d="M244 340C296 324 344 318 387 322C420 325 452 332 478 344" />
            </g>

            <g clip-path="url(#rightClip)">
              <path class="sulcus major" d="M816 120C743 76 659 68 585 88C545 99 515 118 492 146" />
              <path class="sulcus major" d="M830 168C748 130 652 121 572 140C528 149 498 168 478 195" />
              <path class="sulcus major" d="M812 222C730 190 645 184 569 201C523 210 492 228 473 251" />
              <path class="sulcus major" d="M768 279C691 249 618 243 559 257C514 266 483 281 466 299" />

              <path class="sulcus minor" d="M786 99C725 72 660 66 600 76C561 83 530 95 504 113" />
              <path class="sulcus minor" d="M803 139C728 108 653 101 587 112C545 118 512 130 487 147" />
              <path class="sulcus minor" d="M813 188C732 161 651 153 581 162C538 167 503 177 478 193" />
              <path class="sulcus minor" d="M810 241C729 217 651 209 581 217C536 222 500 231 474 247" />
              <path class="sulcus minor" d="M766 297C697 275 633 268 576 274C536 279 500 287 473 300" />
              <path class="sulcus minor" d="M722 340C670 324 622 318 579 322C546 325 514 332 488 344" />
            </g>

            <path *ngFor="let flow of flows" class="edge" [attr.d]="flow.d" />
            <path *ngFor="let flow of flows" [attr.id]="flowPathId(flow.from, flow.to)" class="flow-path" [attr.d]="flow.d" />
            <path
              *ngFor="let flow of flows"
              class="signal"
              [attr.d]="flow.d"
              [style.opacity]="signalOpacity(flow.from, flow.to)"
              [style.animation-duration]="signalDuration(flow.from, flow.to)"></path>

            <circle
              *ngFor="let p of flowParticles"
              class="signal-particle"
              [attr.r]="p.radius"
              [style.opacity]="signalOpacity(p.from, p.to)"
              [style.fill]="p.color"
              [style.filter]="particleGlow(p.color)">
              <animateMotion [attr.dur]="signalDuration(p.from, p.to)" [attr.begin]="p.begin" repeatCount="indefinite" rotate="auto">
                <mpath [attr.href]="'#' + flowPathId(p.from, p.to)" [attr.xlink:href]="'#' + flowPathId(p.from, p.to)"></mpath>
              </animateMotion>
            </circle>

            <g *ngFor="let region of regions">
              <circle
                class="region-hit"
                [class.selected]="selectedRegion === region.key"
                [attr.cx]="region.x"
                [attr.cy]="region.y"
                r="33"
                [attr.fill]="regionFill(region.intensity)"
                [attr.stroke]="selectedRegion === region.key ? 'rgba(247,84,175,0.97)' : 'rgba(161,208,255,0.78)'"
                [attr.stroke-width]="selectedRegion === region.key ? 2.9 : 1.7"
                (mouseenter)="hoverRegion(region.key)"
                (click)="selectRegion(region.key)" />
              <text [attr.x]="region.x" [attr.y]="region.y - 3" text-anchor="middle" class="region-label">{{ region.label }}</text>
              <text [attr.x]="region.x" [attr.y]="region.y + 12" text-anchor="middle" class="region-value">{{ region.intensity | number: '1.2-2' }}</text>
            </g>

            <circle
              *ngIf="selectedRegionView() as selected"
              class="selection-halo"
              [attr.cx]="selected.x"
              [attr.cy]="selected.y"
              [attr.r]="36"
              [style.opacity]="0.22 + selected.intensity * 0.5"></circle>
          </svg>
        </div>

        <div class="hover-tooltip" *ngIf="hoveredRegionView() as hover" [ngStyle]="tooltipStyle(hover)">
          <strong>{{ hover.label }}</strong>
          <span>{{ hover.subtitle }}</span>
          <small>{{ hover.detail }}</small>
        </div>

        <div class="empty-overlay" *ngIf="!hasProfile()">
          <strong>No cognitive profile loaded</strong>
          <span>Create or open settings to initialize this companion's cognitive controls.</span>
        </div>
      </div>

      <section class="detail-panel" *ngIf="!compact && selectedRegionView() as selected">
        <div class="detail-head">
          <h4>{{ selected.label }} Detail</h4>
          <span class="score">{{ selected.intensity | number: '1.2-2' }}</span>
        </div>
        <p>{{ selected.detail }}</p>
        <div class="drivers">
          <span class="driver" *ngFor="let driver of selected.drivers">{{ driver }}</span>
        </div>
      </section>

      <section class="atlas-legend" *ngIf="showLegend">
        <button
          type="button"
          class="legend-item"
          *ngFor="let region of regions"
          [class.active]="region.key === selectedRegion"
          (mouseenter)="hoverRegion(region.key)"
          (mouseleave)="hoveredRegion = null"
          (click)="selectRegion(region.key)">
          <span class="dot" [style.background]="regionFill(region.intensity)"></span>
          <span class="meta">
            <strong>{{ region.label }}</strong>
            <small>{{ region.subtitle }}</small>
          </span>
          <span class="score">{{ region.intensity | number: '1.2-2' }}</span>
        </button>
      </section>
    </article>
  `,
  styles: [
    `
      .atlas-shell {
        overflow: visible;
        border: 1px solid rgba(105, 145, 208, 0.3);
        border-radius: 1rem;
        padding: 0.75rem;
        background: linear-gradient(145deg, rgba(9, 18, 37, 0.9), rgba(8, 15, 30, 0.94));
      }

      .atlas-shell.compact {
        padding: 0.5rem;
      }

      .atlas-shell.empty {
        border-color: rgba(125, 144, 173, 0.3);
      }

      .atlas-header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        gap: 0.65rem;
      }

      .atlas-header h3 {
        margin: 0;
        font-size: 0.9rem;
      }

      .atlas-header p {
        margin: 0.18rem 0 0.5rem;
        font-size: 0.76rem;
        opacity: 0.82;
      }

      .posture-pill {
        border: 1px solid rgba(101, 143, 198, 0.42);
        border-radius: 999px;
        padding: 0.12rem 0.52rem;
        font-size: 0.66rem;
        letter-spacing: 0.04em;
        text-transform: uppercase;
        opacity: 0.9;
      }

      .atlas-visual {
        position: relative;
        overflow: visible;
      }

      .atlas-canvas {
        position: relative;
        border: 1px solid rgba(102, 138, 190, 0.24);
        border-radius: 0.9rem;
        overflow: hidden;
        background: radial-gradient(ellipse at 50% 50%, rgba(18, 37, 70, 0.46), rgba(8, 12, 24, 0.95));
        max-width: 790px;
        margin: 0 auto;
      }

      svg {
        width: 100%;
        height: auto;
        display: block;
      }

      .atlas-shell.compact .atlas-canvas {
        max-width: 600px;
      }

      .brain-mass {
        transform-origin: 50% 50%;
        transform-box: fill-box;
        animation: brain-breathe 6.2s ease-in-out infinite;
      }

      .brain-inner {
        fill: rgba(93, 141, 207, 0.06);
        stroke: rgba(137, 190, 255, 0.16);
        stroke-width: 1;
        transform-origin: 50% 50%;
        transform-box: fill-box;
        animation: brain-breathe-inner 6.2s ease-in-out infinite;
      }

      .brainstem {
        fill: rgba(18, 35, 65, 0.9);
        stroke: rgba(130, 186, 247, 0.36);
        stroke-width: 1.2;
      }

      .sulcus {
        fill: none;
      }

      .sulcus.major {
        stroke: rgba(168, 216, 255, 0.31);
        stroke-width: 1.26;
      }

      .sulcus.minor {
        stroke: rgba(146, 193, 242, 0.23);
        stroke-width: 0.93;
      }

      .midline-fissure {
        fill: none;
        stroke: rgba(197, 226, 255, 0.2);
        stroke-width: 0.9;
        stroke-linecap: round;
      }

      .left-lobe-tint {
        fill: url(#leftLobeTint);
      }

      .right-lobe-tint {
        fill: url(#rightLobeTint);
      }

      .edge {
        fill: none;
        stroke: rgba(142, 184, 255, 0.22);
        stroke-width: 1.22;
      }

      .flow-path {
        fill: none;
        stroke: transparent;
        stroke-width: 1;
      }

      .signal {
        fill: none;
        stroke: rgba(108, 240, 255, 0.36);
        stroke-width: 1.7;
        stroke-linecap: round;
        animation-name: signal-glow;
        animation-iteration-count: infinite;
        animation-timing-function: ease-in-out;
      }

      .signal-particle {
        will-change: transform;
      }

      .selection-halo {
        fill: rgba(247, 84, 175, 0.18);
        stroke: rgba(247, 84, 175, 0.54);
        stroke-width: 1.2;
        transform-box: fill-box;
        transform-origin: center;
        animation: halo-pulse 1.9s ease-in-out infinite;
        pointer-events: none;
      }

      .region-hit {
        cursor: pointer;
        transition: fill 0.2s ease, stroke 0.2s ease;
      }

      .region-hit:hover,
      .region-hit.selected {
        filter: drop-shadow(0 0 12px rgba(91, 210, 255, 0.42));
      }

      .region-label {
        fill: rgba(232, 242, 255, 0.95);
        font-size: 10.7px;
        font-weight: 600;
      }

      .region-value {
        fill: rgba(198, 218, 245, 0.95);
        font-size: 9.5px;
      }

      .hover-tooltip {
        position: absolute;
        pointer-events: none;
        z-index: 40;
        min-width: 190px;
        max-width: 260px;
        border: 1px solid rgba(109, 156, 224, 0.46);
        border-radius: 0.72rem;
        padding: 0.46rem 0.56rem;
        background: rgba(5, 13, 28, 0.96);
        box-shadow: 0 12px 24px rgba(0, 0, 0, 0.42);
        display: grid;
        gap: 0.16rem;
        transform: translate(-8%, -100%);
      }

      .hover-tooltip strong {
        font-size: 0.78rem;
      }

      .hover-tooltip span {
        font-size: 0.72rem;
        opacity: 0.88;
      }

      .hover-tooltip small {
        font-size: 0.68rem;
        opacity: 0.77;
        line-height: 1.25;
      }

      .detail-panel {
        margin-top: 0.56rem;
        border: 1px solid rgba(107, 150, 213, 0.28);
        border-radius: 0.8rem;
        padding: 0.55rem 0.62rem;
        background: rgba(8, 15, 30, 0.78);
      }

      .detail-head {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 0.5rem;
      }

      .detail-head h4 {
        margin: 0;
        font-size: 0.82rem;
      }

      .detail-panel .score {
        font-size: 0.74rem;
        border: 1px solid rgba(116, 165, 231, 0.34);
        border-radius: 999px;
        padding: 0.08rem 0.45rem;
      }

      .detail-panel p {
        margin: 0.34rem 0 0;
        font-size: 0.72rem;
        line-height: 1.35;
        opacity: 0.9;
      }

      .drivers {
        display: flex;
        flex-wrap: wrap;
        gap: 0.35rem;
        margin-top: 0.46rem;
      }

      .driver {
        border: 1px solid rgba(105, 146, 208, 0.3);
        border-radius: 999px;
        padding: 0.12rem 0.42rem;
        font-size: 0.68rem;
        opacity: 0.88;
      }

      .empty-overlay {
        position: absolute;
        inset: auto 0 0 0;
        z-index: 8;
        display: grid;
        gap: 0.14rem;
        background: linear-gradient(180deg, rgba(7, 13, 24, 0), rgba(7, 13, 24, 0.86) 40%);
        padding: 1.2rem 0.8rem 0.7rem;
      }

      .empty-overlay strong {
        font-size: 0.76rem;
      }

      .empty-overlay span {
        font-size: 0.69rem;
        opacity: 0.82;
      }

      .atlas-legend {
        margin-top: 0.55rem;
        display: grid;
        gap: 0.42rem;
      }

      .atlas-shell.compact .atlas-legend {
        grid-template-columns: repeat(2, minmax(0, 1fr));
        gap: 0.36rem;
      }

      .legend-item {
        width: 100%;
        border: 1px solid rgba(98, 131, 183, 0.24);
        border-radius: 0.68rem;
        padding: 0.42rem 0.52rem;
        background: rgba(8, 14, 27, 0.62);
        display: grid;
        grid-template-columns: auto 1fr auto;
        gap: 0.52rem;
        align-items: center;
        text-align: left;
      }

      .atlas-shell.compact .legend-item {
        padding: 0.35rem 0.44rem;
      }

      .legend-item.active {
        border-color: rgba(247, 84, 175, 0.64);
      }

      .dot {
        width: 0.62rem;
        height: 0.62rem;
        border-radius: 999px;
      }

      .meta {
        display: grid;
      }

      .meta strong {
        font-size: 0.78rem;
        line-height: 1.25;
      }

      .meta small {
        font-size: 0.7rem;
        opacity: 0.78;
      }

      .score {
        font-size: 0.78rem;
        opacity: 0.9;
      }

      @keyframes signal-glow {
        0% {
          stroke-opacity: 0.2;
        }
        50% {
          stroke-opacity: 0.7;
        }
        100% {
          stroke-opacity: 0.2;
        }
      }

      @keyframes brain-breathe {
        0% {
          transform: scale(1);
        }
        50% {
          transform: scale(1.01) translateY(-0.8px);
        }
        100% {
          transform: scale(1);
        }
      }

      @keyframes brain-breathe-inner {
        0% {
          transform: scale(0.998);
        }
        50% {
          transform: scale(1.006) translateY(-0.5px);
        }
        100% {
          transform: scale(0.998);
        }
      }

      @keyframes halo-pulse {
        0% {
          transform: scale(0.95);
          transform-origin: center;
        }
        50% {
          transform: scale(1.08);
          transform-origin: center;
        }
        100% {
          transform: scale(0.95);
          transform-origin: center;
        }
      }
    `,
  ],
})
export class CognitiveAtlasComponent {
  @Input() profile: unknown;
  @Input() compact = false;
  @Input() showLegend = true;
  @Input() title = 'Cognitive Atlas';
  @Input() subtitle = 'Live profile visualization';
  @Output() readonly regionSelected = new EventEmitter<RegionKey>();

  readonly flows: FlowPath[] = [
    { from: 'attention', to: 'reasoning', d: 'M290 150Q348 128 410 126' },
    { from: 'reasoning', to: 'memory', d: 'M410 126Q483 114 548 126' },
    { from: 'memory', to: 'reflection', d: 'M548 126Q610 126 672 152' },
    { from: 'reasoning', to: 'uncertainty', d: 'M410 126Q450 202 482 300' },
    { from: 'memory', to: 'uncertainty', d: 'M548 126Q512 202 482 300' },
    { from: 'uncertainty', to: 'expression', d: 'M482 300Q398 302 318 274' },
    { from: 'uncertainty', to: 'adaptation', d: 'M482 300Q566 302 646 274' },
    { from: 'reflection', to: 'adaptation', d: 'M672 152Q664 212 646 274' },
    { from: 'attention', to: 'expression', d: 'M290 150Q292 212 318 274' },
  ];

  readonly flowParticles: FlowParticle[] = this.createFlowParticles();

  selectedRegion: RegionKey = 'reasoning';
  hoveredRegion: RegionKey | null = null;

  get regions(): RegionView[] {
    const profile = this.profileObject();

    return [
      {
        key: 'attention',
        label: 'Attention',
        x: 290,
        y: 150,
        intensity: this.attentionIntensity(),
        subtitle: this.attentionSubtitle(),
        detail: 'Controls focus persistence, context switching, and how often the companion asks clarifying questions.',
        drivers: [
          `focus ${this.num(profile?.attention?.focusStickiness, 0.5).toFixed(2)}`,
          `clarify ${this.num(profile?.attention?.clarificationFrequency, 0.2).toFixed(2)}`,
        ],
      },
      {
        key: 'reasoning',
        label: 'Reasoning',
        x: 410,
        y: 126,
        intensity: this.reasoningIntensity(),
        subtitle: this.reasoningSubtitle(),
        detail: 'Defines reasoning depth, structure style, and how strongly evidence drives final conclusions.',
        drivers: [
          `${String(profile?.reasoning?.reasoningMode ?? 'hybrid')}`,
          `depth ${Math.round(this.num(profile?.reasoning?.depth, 2))}`,
          `strict ${this.num(profile?.reasoning?.evidenceStrictness, 0.7).toFixed(2)}`,
        ],
      },
      {
        key: 'memory',
        label: 'Memory',
        x: 548,
        y: 126,
        intensity: this.memoryIntensity(),
        subtitle: this.memorySubtitle(),
        detail: 'Shapes memory retrieval ranking, candidate pool size, and what gets written as durable memory.',
        drivers: [
          `candidates ${Math.round(this.num(profile?.memory?.maxCandidates, 120))}`,
          `results ${Math.round(this.num(profile?.memory?.maxResults, 20))}`,
          `recency ${this.num(profile?.memory?.retrievalWeights?.recency, 0.8).toFixed(2)}`,
        ],
      },
      {
        key: 'reflection',
        label: 'Reflection',
        x: 672,
        y: 152,
        intensity: this.reflectionIntensity(),
        subtitle: this.reflectionSubtitle(),
        detail: 'Governs self-critique and subconscious debate activity before the companion finalizes answers.',
        drivers: [
          `critique ${this.num(profile?.reflection?.selfCritiqueRate, 0.25).toFixed(2)}`,
          `turns ${Math.round(this.num(profile?.reflection?.debate?.turnCap, 8))}`,
          `trigger ${this.num(profile?.reflection?.debate?.triggerSensitivity, 0.55).toFixed(2)}`,
        ],
      },
      {
        key: 'expression',
        label: 'Expression',
        x: 318,
        y: 274,
        intensity: this.expressionIntensity(),
        subtitle: this.expressionSubtitle(),
        detail: 'Controls response style, emotional tone, and whether outputs are concise, balanced, or deeply detailed.',
        drivers: [
          `${String(profile?.expression?.verbosityTarget ?? 'balanced')}`,
          `emotion ${this.num(profile?.expression?.emotionalExpressivity, 0.2).toFixed(2)}`,
        ],
      },
      {
        key: 'uncertainty',
        label: 'Uncertainty',
        x: 482,
        y: 300,
        intensity: this.uncertaintyIntensity(),
        subtitle: this.uncertaintySubtitle(),
        detail: 'Determines confidence thresholds for answering, clarifying, deferring, and high-risk evidence behavior.',
        drivers: [
          `answer ${this.num(profile?.uncertainty?.answerConfidenceThreshold, 0.66).toFixed(2)}`,
          `defer ${this.num(profile?.uncertainty?.deferConfidenceThreshold, 0.3).toFixed(2)}`,
          `clarify ${this.num(profile?.uncertainty?.clarifyConfidenceThreshold, 0.5).toFixed(2)}`,
        ],
      },
      {
        key: 'adaptation',
        label: 'Adaptation',
        x: 646,
        y: 274,
        intensity: this.adaptationIntensity(),
        subtitle: this.adaptationSubtitle(),
        detail: 'Balances procedural consistency vs adaptation speed and how aggressively the companion evolves over time.',
        drivers: [
          `adapt ${this.num(profile?.adaptation?.adaptivity, 0.42).toFixed(2)}`,
          `policy ${this.num(profile?.adaptation?.policyStrictness, 0.65).toFixed(2)}`,
          `delta ${this.num(profile?.evolution?.maxDailyDelta, 0.06).toFixed(2)}`,
        ],
      },
    ];
  }

  hasProfile(): boolean {
    return !!(this.profile && typeof this.profile === 'object' && Object.keys(this.profile as Record<string, unknown>).length > 0);
  }

  postureText(): string {
    const avg = this.regions.reduce((sum, region) => sum + region.intensity, 0) / this.regions.length;
    if (avg < 0.38) {
      return 'Reserved';
    }

    if (avg < 0.68) {
      return 'Balanced';
    }

    return 'Intense';
  }

  regionFill(intensity: number): string {
    const clamped = this.clamp01(intensity);
    const alpha = 0.2 + (clamped * 0.66);
    const r = Math.round(86 + (clamped * 168));
    const g = Math.round(214 - (clamped * 84));
    const b = Math.round(255 - (clamped * 96));
    return `rgba(${r}, ${g}, ${b}, ${alpha.toFixed(3)})`;
  }

  hoverRegion(key: RegionKey): void {
    this.hoveredRegion = key;
  }

  selectedRegionView(): RegionView | null {
    return this.regions.find((region) => region.key === this.selectedRegion) ?? null;
  }

  hoveredRegionView(): RegionView | null {
    if (!this.hoveredRegion) {
      return null;
    }

    return this.regions.find((region) => region.key === this.hoveredRegion) ?? null;
  }

  tooltipStyle(region: RegionView): Record<string, string> {
    const leftPercent = this.clamp((region.x / 960) * 100, 12, 88);
    const topPercent = this.clamp(((region.y / 420) * 100) - 8, 11, 84);

    return {
      left: `${leftPercent}%`,
      top: `${topPercent}%`,
    };
  }

  signalOpacity(from: RegionKey, to: RegionKey): number {
    const strength = (this.regionIntensity(from) + this.regionIntensity(to)) / 2;
    return 0.16 + (strength * 0.74);
  }

  signalDuration(from: RegionKey, to: RegionKey): string {
    const strength = (this.regionIntensity(from) + this.regionIntensity(to)) / 2;
    const seconds = 4.5 - (strength * 2.5);
    return `${Math.max(1.15, seconds).toFixed(2)}s`;
  }

  flowPathId(from: RegionKey, to: RegionKey): string {
    return `flow-${from}-${to}`;
  }

  particleGlow(color: string): string {
    return `drop-shadow(0 0 6px ${color})`;
  }

  selectRegion(key: RegionKey): void {
    this.selectedRegion = key;
    this.regionSelected.emit(key);
  }

  private createFlowParticles(): FlowParticle[] {
    const palette = ['rgba(132,245,255,0.96)', 'rgba(248,102,188,0.92)', 'rgba(182,220,255,0.92)'];
    const particles: FlowParticle[] = [];

    let index = 0;
    for (const flow of this.flows) {
      for (const offset of [0, 0.78]) {
        particles.push({
          from: flow.from,
          to: flow.to,
          begin: `-${(index * 0.29 + offset).toFixed(2)}s`,
          radius: 2.1 + ((index % 3) * 0.45),
          color: palette[index % palette.length],
        });
        index += 1;
      }
    }

    return particles;
  }

  private profileObject(): any {
    return this.profile && typeof this.profile === 'object' ? this.profile : {};
  }

  private attentionIntensity(): number {
    const profile = this.profileObject();
    const focus = this.num(profile?.attention?.focusStickiness, 0.5);
    const clarify = this.num(profile?.attention?.clarificationFrequency, 0.2);
    return this.clamp01((focus * 0.7) + (clarify * 0.3));
  }

  private memoryIntensity(): number {
    const profile = this.profileObject();
    const recency = this.num(profile?.memory?.retrievalWeights?.recency, 0.8) / 1.5;
    const semantic = this.num(profile?.memory?.retrievalWeights?.semanticMatch, 1) / 1.5;
    const confidence = this.num(profile?.memory?.retrievalWeights?.confidence, 0.65);
    return this.clamp01((recency + semantic + confidence) / 3);
  }

  private reasoningIntensity(): number {
    const profile = this.profileObject();
    const depth = this.num(profile?.reasoning?.depth, 2) / 4;
    const strictness = this.num(profile?.reasoning?.evidenceStrictness, 0.7);
    return this.clamp01((depth * 0.46) + (strictness * 0.54));
  }

  private reflectionIntensity(): number {
    const profile = this.profileObject();
    const enabled = profile?.reflection?.selfCritiqueEnabled === false ? 0 : 1;
    const critiqueRate = this.num(profile?.reflection?.selfCritiqueRate, 0.25);
    const debateTrigger = this.num(profile?.reflection?.debate?.triggerSensitivity, 0.55);
    return this.clamp01(enabled * ((critiqueRate * 0.6) + (debateTrigger * 0.4)));
  }

  private expressionIntensity(): number {
    const profile = this.profileObject();
    const emotion = this.num(profile?.expression?.emotionalExpressivity, 0.2);
    const verbosity = String(profile?.expression?.verbosityTarget ?? 'balanced');
    const verbosityScore = verbosity === 'concise' ? 0.33 : verbosity === 'detailed' ? 0.9 : 0.62;
    return this.clamp01((emotion * 0.62) + (verbosityScore * 0.38));
  }

  private uncertaintyIntensity(): number {
    const profile = this.profileObject();
    const answer = this.num(profile?.uncertainty?.answerConfidenceThreshold, 0.66);
    const clarify = this.num(profile?.uncertainty?.clarifyConfidenceThreshold, 0.5);
    const defer = this.num(profile?.uncertainty?.deferConfidenceThreshold, 0.3);
    return this.clamp01((answer * 0.45) + (clarify * 0.25) + (defer * 0.3));
  }

  private adaptationIntensity(): number {
    const profile = this.profileObject();
    const adaptivity = this.num(profile?.adaptation?.adaptivity, 0.42);
    const dailyDelta = this.num(profile?.evolution?.maxDailyDelta, 0.06) / 0.2;
    return this.clamp01((adaptivity * 0.68) + (dailyDelta * 0.32));
  }

  private regionIntensity(key: RegionKey): number {
    if (key === 'attention') {
      return this.attentionIntensity();
    }

    if (key === 'memory') {
      return this.memoryIntensity();
    }

    if (key === 'reasoning') {
      return this.reasoningIntensity();
    }

    if (key === 'reflection') {
      return this.reflectionIntensity();
    }

    if (key === 'expression') {
      return this.expressionIntensity();
    }

    if (key === 'uncertainty') {
      return this.uncertaintyIntensity();
    }

    return this.adaptationIntensity();
  }

  private attentionSubtitle(): string {
    const profile = this.profileObject();
    return `Focus ${this.num(profile?.attention?.focusStickiness, 0.5).toFixed(2)} · Clarify ${this.num(profile?.attention?.clarificationFrequency, 0.2).toFixed(2)}`;
  }

  private memorySubtitle(): string {
    const profile = this.profileObject();
    return `Candidates ${Math.round(this.num(profile?.memory?.maxCandidates, 120))} · Results ${Math.round(this.num(profile?.memory?.maxResults, 20))}`;
  }

  private reasoningSubtitle(): string {
    const profile = this.profileObject();
    const mode = String(profile?.reasoning?.reasoningMode ?? 'hybrid');
    return `${mode} · depth ${Math.round(this.num(profile?.reasoning?.depth, 2))}`;
  }

  private reflectionSubtitle(): string {
    const profile = this.profileObject();
    return `Critique ${this.num(profile?.reflection?.selfCritiqueRate, 0.25).toFixed(2)} · turns ${Math.round(this.num(profile?.reflection?.debate?.turnCap, 8))}`;
  }

  private expressionSubtitle(): string {
    const profile = this.profileObject();
    return `${String(profile?.expression?.verbosityTarget ?? 'balanced')} · emotion ${this.num(profile?.expression?.emotionalExpressivity, 0.2).toFixed(2)}`;
  }

  private uncertaintySubtitle(): string {
    const profile = this.profileObject();
    return `Answer ${this.num(profile?.uncertainty?.answerConfidenceThreshold, 0.66).toFixed(2)} · Defer ${this.num(profile?.uncertainty?.deferConfidenceThreshold, 0.3).toFixed(2)}`;
  }

  private adaptationSubtitle(): string {
    const profile = this.profileObject();
    return `Adapt ${this.num(profile?.adaptation?.adaptivity, 0.42).toFixed(2)} · delta ${this.num(profile?.evolution?.maxDailyDelta, 0.06).toFixed(2)}`;
  }

  private num(value: unknown, fallback: number): number {
    const n = Number(value);
    return Number.isFinite(n) ? n : fallback;
  }

  private clamp(value: number, min: number, max: number): number {
    return Math.max(min, Math.min(max, value));
  }

  private clamp01(value: number): number {
    return Math.max(0, Math.min(1, value));
  }
}
