import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface CompanionProfile {
  companionId: string;
  userId: string;
  name: string;
  tone: string;
  purpose: string;
  modelHint: string;
  sessionId: string;
  originStory: string;
  birthDateUtc?: string | null;
  initialMemoryText?: string | null;
  metadataJson: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  isArchived: boolean;
}

export interface CreateCompanionPayload {
  name: string;
  tone: string;
  purpose: string;
  modelHint: string;
  originStory: string;
  birthDateUtc?: string | null;
  initialMemoryText?: string | null;
  templateKey?: string | null;
  systemPrompt?: string | null;
  metadataJson?: string | null;
  cognitiveProfileJson?: string | null;
}

export interface CompanionCognitiveProfileState {
  companionId: string;
  activeProfileVersionId: string;
  stagedProfileVersionId?: string | null;
  activeVersionNumber: number;
  schemaVersion: string;
  validationStatus: string;
  updatedAtUtc: string;
  updatedByUserId: string;
}

export interface CompanionCognitiveProfileVersion {
  profileVersionId: string;
  companionId: string;
  versionNumber: number;
  schemaVersion: string;
  validationStatus: string;
  profileHash: string;
  createdByUserId: string;
  changeSummary?: string | null;
  changeReason?: string | null;
  createdAtUtc: string;
  profileJson: string;
  compiledRuntimeJson: string;
}

export interface CompanionCognitiveProfileView {
  state: CompanionCognitiveProfileState;
  active?: CompanionCognitiveProfileVersion | null;
}

@Injectable({ providedIn: 'root' })
export class ClientPortalService {
  private readonly http = inject(HttpClient);

  async listCompanions(baseUrl: string, includeArchived = false): Promise<CompanionProfile[]> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions?includeArchived=${includeArchived ? 'true' : 'false'}`;
    return firstValueFrom(this.http.get<CompanionProfile[]>(url));
  }

  async createCompanion(baseUrl: string, payload: CreateCompanionPayload): Promise<CompanionProfile> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions`;
    return firstValueFrom(
      this.http.post<CompanionProfile>(url, {
        name: payload.name,
        tone: payload.tone,
        purpose: payload.purpose,
        modelHint: payload.modelHint,
        originStory: payload.originStory,
        birthDateUtc: payload.birthDateUtc ?? null,
        initialMemoryText: payload.initialMemoryText ?? null,
        cognitiveProfileJson: payload.cognitiveProfileJson ?? null,
        templateKey: payload.templateKey ?? null,
        systemPrompt: payload.systemPrompt ?? null,
        metadataJson: payload.metadataJson ?? null,
      }),
    );
  }

  async archiveCompanion(baseUrl: string, companionId: string): Promise<void> {
    const id = companionId.trim();
    if (!id) {
      return;
    }

    const url = `${this.normalizeBaseUrl(baseUrl)}/companions/${encodeURIComponent(id)}`;
    await firstValueFrom(this.http.delete(url));
  }

  async getCognitiveProfile(baseUrl: string, companionId: string): Promise<CompanionCognitiveProfileView> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions/${encodeURIComponent(companionId)}/cognitive-profile`;
    return firstValueFrom(this.http.get<CompanionCognitiveProfileView>(url));
  }

  async listCognitiveProfileVersions(baseUrl: string, companionId: string, take = 50): Promise<CompanionCognitiveProfileVersion[]> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions/${encodeURIComponent(companionId)}/cognitive-profile/versions?take=${Math.max(1, take)}`;
    return firstValueFrom(this.http.get<CompanionCognitiveProfileVersion[]>(url));
  }

  async validateCognitiveProfile(baseUrl: string, companionId: string, profile: unknown): Promise<{ isValid: boolean; errors: string[]; warnings: string[]; normalizedProfile?: unknown }> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions/${encodeURIComponent(companionId)}/cognitive-profile/validate`;
    return firstValueFrom(this.http.post<{ isValid: boolean; errors: string[]; warnings: string[]; normalizedProfile?: unknown }>(url, { profile }));
  }

  async createCognitiveProfileVersion(
    baseUrl: string,
    companionId: string,
    profile: unknown,
    changeSummary?: string | null,
    changeReason?: string | null,
    validateOnly = false,
  ): Promise<CompanionCognitiveProfileVersion> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions/${encodeURIComponent(companionId)}/cognitive-profile/versions`;
    return firstValueFrom(
      this.http.post<CompanionCognitiveProfileVersion>(url, {
        profile,
        changeSummary: changeSummary ?? null,
        changeReason: changeReason ?? null,
        validateOnly,
      }),
    );
  }

  async activateCognitiveProfile(baseUrl: string, companionId: string, profileVersionId: string, reason?: string | null): Promise<CompanionCognitiveProfileState> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions/${encodeURIComponent(companionId)}/cognitive-profile/activate`;
    return firstValueFrom(this.http.post<CompanionCognitiveProfileState>(url, { profileVersionId, reason: reason ?? null }));
  }

  async rollbackCognitiveProfile(baseUrl: string, companionId: string, targetProfileVersionId: string, reason?: string | null): Promise<CompanionCognitiveProfileState> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions/${encodeURIComponent(companionId)}/cognitive-profile/rollback`;
    return firstValueFrom(this.http.post<CompanionCognitiveProfileState>(url, { targetProfileVersionId, reason: reason ?? null }));
  }

  async listCognitiveAudits(baseUrl: string, companionId: string, take = 120): Promise<unknown[]> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions/${encodeURIComponent(companionId)}/cognitive-profile/audit?take=${Math.max(1, take)}`;
    return firstValueFrom(this.http.get<unknown[]>(url));
  }

  async listCognitiveRuntimeTraces(baseUrl: string, companionId: string, take = 120): Promise<unknown[]> {
    const url = `${this.normalizeBaseUrl(baseUrl)}/companions/${encodeURIComponent(companionId)}/cognitive-profile/runtime-traces?take=${Math.max(1, take)}`;
    return firstValueFrom(this.http.get<unknown[]>(url));
  }

  private normalizeBaseUrl(value: string): string {
    const trimmed = value.trim();
    if (!trimmed) {
      return '/api';
    }

    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
  }
}
