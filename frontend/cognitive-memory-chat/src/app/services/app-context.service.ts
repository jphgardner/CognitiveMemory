import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AppContextService {
  private readonly apiBaseKey = 'cm.app.apiBase.v1';
  private readonly selectedCompanionKey = 'cm.app.selectedCompanionId.v1';

  readonly apiBase = signal(this.load(this.apiBaseKey, '/api'));
  readonly selectedCompanionId = signal<string | null>(this.loadNullable(this.selectedCompanionKey));

  setApiBase(value: string): void {
    const normalized = this.normalizeBase(value);
    this.apiBase.set(normalized);
    localStorage.setItem(this.apiBaseKey, normalized);
  }

  setSelectedCompanionId(value: string | null): void {
    const normalized = value?.trim() || null;
    this.selectedCompanionId.set(normalized);
    if (normalized) {
      localStorage.setItem(this.selectedCompanionKey, normalized);
    } else {
      localStorage.removeItem(this.selectedCompanionKey);
    }
  }

  private normalizeBase(value: string): string {
    const trimmed = value.trim();
    if (!trimmed) {
      return '/api';
    }

    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
  }

  private load(key: string, fallback: string): string {
    const raw = localStorage.getItem(key)?.trim();
    return raw && raw.length > 0 ? raw : fallback;
  }

  private loadNullable(key: string): string | null {
    const raw = localStorage.getItem(key)?.trim();
    return raw && raw.length > 0 ? raw : null;
  }
}
