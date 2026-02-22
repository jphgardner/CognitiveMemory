import { Injectable, inject } from '@angular/core';
import { EventingRow, ScheduledActionRow, SubconsciousDebateRow, SubconsciousLifecycleRow } from '../models/console.models';
import { AuthService } from './auth.service';

@Injectable({ providedIn: 'root' })
export class EventStreamService {
  private readonly auth = inject(AuthService);

  openEventingStream(baseUrl: string, companionId: string, onSnapshot: (rows: EventingRow[]) => void, onError?: () => void): EventSource {
    const query = this.buildQuery({ companionId, take: '180' });
    const source = new EventSource(`${baseUrl}/eventing/events/stream?${query.toString()}`);

    source.addEventListener('snapshot', (event) => {
      const payload = (event as MessageEvent).data;
      if (typeof payload !== 'string' || payload.length === 0) {
        return;
      }

      try {
        onSnapshot(JSON.parse(payload) as EventingRow[]);
      } catch {
        // ignore malformed payload
      }
    });

    source.onerror = () => {
      onError?.();
    };

    return source;
  }

  openSubconsciousStream(
    baseUrl: string,
    sessionId: string,
    onSnapshot: (rows: SubconsciousDebateRow[]) => void,
    onLifecycle: (row: SubconsciousLifecycleRow) => void,
    onError?: () => void,
  ): EventSource {
    const query = this.buildQuery({ sessionId, take: '40' });
    const source = new EventSource(`${baseUrl}/subconscious/debates/stream?${query.toString()}`);

    source.addEventListener('snapshot', (event) => {
      const payload = (event as MessageEvent).data;
      if (typeof payload !== 'string' || payload.length === 0) {
        return;
      }

      try {
        onSnapshot(JSON.parse(payload) as SubconsciousDebateRow[]);
      } catch {
        // ignore malformed payload
      }
    });

    source.addEventListener('lifecycle', (event) => {
      const payload = (event as MessageEvent).data;
      if (typeof payload !== 'string' || payload.length === 0) {
        return;
      }

      try {
        onLifecycle(JSON.parse(payload) as SubconsciousLifecycleRow);
      } catch {
        // ignore malformed payload
      }
    });

    source.onerror = () => {
      onError?.();
    };

    return source;
  }

  openScheduledActionsStream(baseUrl: string, companionId: string, onSnapshot: (rows: ScheduledActionRow[]) => void, onError?: () => void): EventSource {
    const query = this.buildQuery({ companionId, take: '250' });
    const source = new EventSource(`${baseUrl}/scheduled-actions/stream?${query.toString()}`);

    source.addEventListener('snapshot', (event) => {
      const payload = (event as MessageEvent).data;
      if (typeof payload !== 'string' || payload.length === 0) {
        return;
      }

      try {
        onSnapshot(JSON.parse(payload) as ScheduledActionRow[]);
      } catch {
        // ignore malformed payload
      }
    });

    source.onerror = () => {
      onError?.();
    };

    return source;
  }

  private buildQuery(values: Record<string, string>): URLSearchParams {
    const query = new URLSearchParams(values);
    const token = this.auth.token;
    if (token) {
      query.set('access_token', token);
    }

    return query;
  }
}
