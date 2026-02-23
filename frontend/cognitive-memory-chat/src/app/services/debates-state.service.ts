import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  DebateEventRow,
  DebateOutcomeRow,
  DebateReviewResponse,
  DebateTurnRow,
  EventingRow,
  SubconsciousDebateRow,
} from '../models/console.models';

@Injectable({ providedIn: 'root' })
export class DebatesStateService {
  private readonly http = inject(HttpClient);

  list(baseUrl: string, sessionId: string, take = 120): Promise<SubconsciousDebateRow[]> {
    return firstValueFrom(
      this.http.get<SubconsciousDebateRow[]>(`${baseUrl}/subconscious/debates/${encodeURIComponent(sessionId)}?take=${take}`),
    );
  }

  detail(baseUrl: string, debateId: string): Promise<SubconsciousDebateRow | null> {
    return this.getOrNull<SubconsciousDebateRow>(`${baseUrl}/subconscious/debates/detail/${encodeURIComponent(debateId)}`);
  }

  turns(baseUrl: string, debateId: string): Promise<DebateTurnRow[]> {
    return this.getOrDefault<DebateTurnRow[]>(`${baseUrl}/subconscious/debates/${encodeURIComponent(debateId)}/turns`, []);
  }

  outcome(baseUrl: string, debateId: string): Promise<DebateOutcomeRow | null> {
    return this.getOrNull<DebateOutcomeRow>(`${baseUrl}/subconscious/debates/${encodeURIComponent(debateId)}/outcome`);
  }

  review(baseUrl: string, debateId: string): Promise<DebateReviewResponse | null> {
    return this.getOrNull<DebateReviewResponse>(`${baseUrl}/subconscious/debates/${encodeURIComponent(debateId)}/review`);
  }

  async events(baseUrl: string, debateId: string, companionId: string, endpointAvailable: boolean): Promise<{ rows: DebateEventRow[]; endpointAvailable: boolean }> {
    const encodedDebateId = encodeURIComponent(debateId);
    let available = endpointAvailable;

    if (available) {
      try {
        const rows = await firstValueFrom(
          this.http.get<DebateEventRow[]>(`${baseUrl}/subconscious/debates/${encodedDebateId}/events?take=200`),
        );
        return { rows, endpointAvailable: available };
      } catch (error) {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          available = false;
        } else {
          return { rows: [], endpointAvailable: available };
        }
      }
    }

    const normalizedCompanionId = companionId.trim();
    if (!normalizedCompanionId) {
      return { rows: [], endpointAvailable: available };
    }

    try {
      const rows = await firstValueFrom(
        this.http.get<EventingRow[]>(`${baseUrl}/eventing/events?companionId=${encodeURIComponent(normalizedCompanionId)}&take=300`),
      );
      const debateToken = debateId.toLowerCase();
      const aggregateToken = debateId.replace(/-/g, '').toLowerCase();

      const mapped = rows
        .filter(
          (x) =>
            (x.aggregateType ?? '') === 'SubconsciousDebate'
            && (
              (x.aggregateId ?? '').toLowerCase() === aggregateToken
              || (x.payloadPreview ?? '').toLowerCase().includes(debateToken)),
        )
        .map(
          (x) =>
            ({
              eventId: x.eventId,
              eventType: x.eventType,
              status: x.status,
              retryCount: x.retryCount,
              lastError: x.lastError,
              occurredAtUtc: x.occurredAtUtc,
              publishedAtUtc: x.publishedAtUtc,
              payloadPreview: x.payloadPreview,
            }) satisfies DebateEventRow,
        );

      return { rows: mapped, endpointAvailable: available };
    } catch {
      return { rows: [], endpointAvailable: available };
    }
  }

  async decide(baseUrl: string, debateId: string, action: 'approve' | 'reject', userInput: string | null, queueRerun: boolean): Promise<void> {
    await firstValueFrom(
      this.http.post(`${baseUrl}/subconscious/debates/${debateId}/decision`, {
        action,
        userInput,
        queueRerun,
      }),
    );
  }

  async rerun(baseUrl: string, request: { sessionId: string; topicKey: string; triggerEventType: string; triggerPayloadJson: string }): Promise<void> {
    await firstValueFrom(this.http.post(`${baseUrl}/subconscious/debates/run-once`, request));
  }

  private async getOrNull<T>(url: string): Promise<T | null> {
    try {
      return await firstValueFrom(this.http.get<T>(url));
    } catch {
      return null;
    }
  }

  private async getOrDefault<T>(url: string, fallback: T): Promise<T> {
    try {
      return await firstValueFrom(this.http.get<T>(url));
    } catch {
      return fallback;
    }
  }
}
