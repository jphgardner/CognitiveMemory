import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class SessionContextService {
  private readonly key = 'cm.session_id';

  getOrCreate(): string {
    const current = this.getStored();
    if (current) {
      return current;
    }

    const generated = this.generateSessionId();
    this.save(generated);
    return generated;
  }

  getStored(): string | null {
    try {
      const value = localStorage.getItem(this.key)?.trim();
      return value ? value : null;
    } catch {
      return null;
    }
  }

  save(sessionId: string): void {
    const normalized = sessionId.trim();
    if (!normalized) {
      return;
    }

    try {
      localStorage.setItem(this.key, normalized);
    } catch {
      // ignore storage failures
    }
  }

  private generateSessionId(): string {
    if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) {
      return crypto.randomUUID();
    }

    return `session-${Date.now()}`;
  }
}
