import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AppContextService } from '../services/app-context.service';
import { AuthService } from '../services/auth.service';
import { ClientPortalService, CompanionProfile, CreateCompanionPayload } from '../services/client-portal.service';
import { SessionContextService } from '../services/session-context.service';
import { CognitiveAtlasComponent } from '../components/cognitive-atlas.component';
import { CompanionSettingsDialogComponent } from './companion-settings-dialog.component';
import { CreateCompanionDialogComponent } from './create-companion-dialog.component';

@Component({
  selector: 'app-client-portal',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, CognitiveAtlasComponent],
  templateUrl: './client-portal.component.html',
})
export class ClientPortalComponent implements OnInit {
  private readonly portal = inject(ClientPortalService);
  private readonly auth = inject(AuthService);
  private readonly sessions = inject(SessionContextService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly dialog = inject(MatDialog);
  private readonly context = inject(AppContextService);

  readonly companions = signal<CompanionProfile[]>([]);
  readonly selectedCompanionId = signal<string | null>(null);
  readonly loadingCompanions = signal(false);
  readonly authUserEmail = signal('');
  readonly selectedCognitiveProfile = signal<unknown | null>(null);
  readonly cognitiveSnapshot = computed(() => {
    const profile = this.selectedCognitiveProfile() as any;
    if (!profile || typeof profile !== 'object') {
      return {
        verbosity: 'n/a',
        reasoning: 'n/a',
        confidence: 'n/a',
        adaptation: 'n/a',
      };
    }

    const verbosity = String(profile?.expression?.verbosityTarget ?? 'balanced');
    const reasoning = String(profile?.reasoning?.reasoningMode ?? 'hybrid');
    const confidenceRaw = Number(profile?.uncertainty?.answerConfidenceThreshold ?? NaN);
    const adaptationRaw = Number(profile?.adaptation?.adaptivity ?? NaN);

    return {
      verbosity,
      reasoning,
      confidence: Number.isFinite(confidenceRaw) ? confidenceRaw.toFixed(2) : 'n/a',
      adaptation: Number.isFinite(adaptationRaw) ? adaptationRaw.toFixed(2) : 'n/a',
    };
  });
  readonly selectedCompanion = computed(() => {
    const selectedCompanionId = this.selectedCompanionId();
    if (!selectedCompanionId) {
      return null;
    }

    return this.companions().find((x) => x.companionId === selectedCompanionId) ?? null;
  });

  ngOnInit(): void {
    this.route.paramMap.subscribe((params) => {
      this.selectedCompanionId.set(params.get('companionId'));
      void this.loadSelectedCompanionCognitiveProfile();
    });

    void this.tryRestoreSession();
  }

  logout(): Promise<boolean> {
    this.auth.logout();
    this.companions.set([]);
    this.selectedCompanionId.set(null);
    return this.router.navigate(['/auth/login']);
  }

  async openCreateDialog(): Promise<void> {
    const ref = this.dialog.open(CreateCompanionDialogComponent, {
      width: '900px',
      maxWidth: '96vw',
      maxHeight: '94vh',
      panelClass: 'create-companion-dialog',
      autoFocus: false,
    });

    const payload = await firstValueFrom(ref.afterClosed());
    if (!payload) {
      return;
    }

    await this.createCompanion(payload);
  }

  async createCompanion(payload: CreateCompanionPayload): Promise<void> {
    const created = await this.portal.createCompanion(this.baseUrl(), payload);
    await this.reloadCompanions(created.companionId);
    await this.router.navigate(['/portal', created.companionId]);
  }

  async selectCompanion(companionId: string): Promise<void> {
    this.context.setSelectedCompanionId(companionId);
    void this.loadSelectedCompanionCognitiveProfile(companionId);
    await this.router.navigate(['/portal', companionId]);
  }

  openWorkspace(companionId: string): void {
    this.context.setSelectedCompanionId(companionId);
    void this.router.navigate(['/workspace', companionId]);
  }

  async openSettings(companionId?: string): Promise<void> {
    const targetId = companionId ?? this.selectedCompanionId();
    if (!targetId) {
      return;
    }

    const companion = this.companions().find((x) => x.companionId === targetId);
    if (!companion) {
      return;
    }

    const ref = this.dialog.open(CompanionSettingsDialogComponent, {
      width: '1080px',
      maxWidth: '97vw',
      maxHeight: '94vh',
      panelClass: 'companion-settings-dialog',
      autoFocus: false,
      data: {
        companion,
        baseUrl: this.baseUrl(),
      },
    });

    const changed = await firstValueFrom(ref.afterClosed());
    if (changed) {
      await this.reloadCompanions(companion.companionId);
      void this.loadSelectedCompanionCognitiveProfile(companion.companionId);
    }
  }

  async deleteCompanion(companionId: string): Promise<void> {
    await this.portal.archiveCompanion(this.baseUrl(), companionId);
    await this.reloadCompanions();

    if (this.selectedCompanionId() === companionId) {
      const next = this.companions()[0]?.companionId;
      if (next) {
        void this.router.navigate(['/portal', next]);
      } else {
        void this.router.navigate(['/portal']);
      }
    }
  }

  openEnterpriseConsole(): void {
    const selected = this.selectedCompanion();
    if (!selected) {
      return;
    }

    this.context.setSelectedCompanionId(selected.companionId);
    this.sessions.save(selected.sessionId);
    void this.router.navigate(['/console', 'chat']);
  }

  private async tryRestoreSession(): Promise<void> {
    const authUser = await this.auth.loadMe(this.baseUrl());
    if (!authUser) {
      await this.router.navigate(['/auth/login']);
      return;
    }

    this.authUserEmail.set(authUser.email);
    await this.reloadCompanions();
  }

  private async reloadCompanions(preferredCompanionId?: string): Promise<void> {
    this.loadingCompanions.set(true);
    try {
      const rows = await this.portal.listCompanions(this.baseUrl(), false);
      this.companions.set(rows);

      const preferred = preferredCompanionId ?? this.selectedCompanionId();
      if (preferred && rows.some((x) => x.companionId === preferred)) {
        this.selectedCompanionId.set(preferred);
      } else {
        this.selectedCompanionId.set(rows[0]?.companionId ?? null);
      }

      this.context.setSelectedCompanionId(this.selectedCompanionId());
      await this.loadSelectedCompanionCognitiveProfile();
    } finally {
      this.loadingCompanions.set(false);
    }
  }

  private async loadSelectedCompanionCognitiveProfile(companionId?: string | null): Promise<void> {
    const targetId = companionId ?? this.selectedCompanionId();
    if (!targetId) {
      this.selectedCognitiveProfile.set(null);
      return;
    }

    try {
      const view = await this.portal.getCognitiveProfile(this.baseUrl(), targetId);
      const activeJson = view.active?.profileJson;
      if (!activeJson) {
        this.selectedCognitiveProfile.set(null);
        return;
      }

      this.selectedCognitiveProfile.set(JSON.parse(activeJson));
    } catch {
      this.selectedCognitiveProfile.set(null);
    }
  }

  private baseUrl(): string {
    const trimmed = this.context.apiBase().trim();
    if (!trimmed) {
      return '/api';
    }

    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
  }
}
