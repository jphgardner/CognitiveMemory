import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { MemoryRelationshipRow } from '../models/console.models';
export interface MemoryNodeDetail {
  found: boolean;
  nodeType: number;
  nodeId: string;
  title?: string | null;
  value?: string | null;
  summary?: string | null;
  updatedAtUtc?: string | null;
}

@Injectable({ providedIn: 'root' })
export class MemoryStateService {
  private readonly http = inject(HttpClient);

  async loadBySession(baseUrl: string, companionId: string, sessionId: string, take: number, relationshipType?: string): Promise<MemoryRelationshipRow[]> {
    const query = new URLSearchParams();
    query.set('companionId', companionId.trim());
    query.set('sessionId', sessionId.trim());
    query.set('take', String(Math.max(1, Math.min(take, 1000))));
    if (relationshipType && relationshipType.trim()) {
      query.set('relationshipType', relationshipType.trim());
    }

    return firstValueFrom(this.http.get<MemoryRelationshipRow[]>(`${baseUrl}/relationships/by-session?${query.toString()}`));
  }

  async loadByNode(baseUrl: string, companionId: string, sessionId: string, nodeType: number, nodeId: string, take: number, relationshipType?: string): Promise<MemoryRelationshipRow[]> {
    const query = new URLSearchParams();
    query.set('companionId', companionId.trim());
    query.set('sessionId', sessionId.trim());
    query.set('nodeType', String(nodeType));
    query.set('nodeId', nodeId.trim());
    query.set('take', String(Math.max(1, Math.min(take, 1000))));
    if (relationshipType && relationshipType.trim()) {
      query.set('relationshipType', relationshipType.trim());
    }

    return firstValueFrom(this.http.get<MemoryRelationshipRow[]>(`${baseUrl}/relationships/by-node?${query.toString()}`));
  }

  async create(baseUrl: string, request: {
    companionId: string;
    sessionId: string;
    fromType: number;
    fromId: string;
    toType: number;
    toId: string;
    relationshipType: string;
    confidence: number;
    strength: number;
  }): Promise<void> {
    await firstValueFrom(this.http.post(`${baseUrl}/relationships`, request));
  }

  async retire(baseUrl: string, relationshipId: string): Promise<void> {
    await firstValueFrom(this.http.post(`${baseUrl}/relationships/${relationshipId}/retire`, {}));
  }

  async backfill(baseUrl: string, sessionId: string | null, take: number): Promise<void> {
    await firstValueFrom(
      this.http.post(`${baseUrl}/relationships/backfill/run-once`, {
        sessionId,
        take: Math.max(100, Math.min(take, 10000)),
      }),
    );
  }

  async extract(baseUrl: string, sessionId: string, take: number, apply: boolean): Promise<void> {
    await firstValueFrom(
      this.http.post(`${baseUrl}/relationships/extract/run-once`, {
        sessionId,
        take: Math.max(20, Math.min(take, 2000)),
        apply,
      }),
    );
  }

  async resolveNodeDetail(baseUrl: string, companionId: string, nodeType: number, nodeId: string): Promise<MemoryNodeDetail> {
    const query = new URLSearchParams();
    query.set('nodeType', String(nodeType));
    query.set('nodeId', nodeId.trim());
    return firstValueFrom(
      this.http.get<MemoryNodeDetail>(`${baseUrl}/workspace/companion/${encodeURIComponent(companionId)}/memory-node-detail?${query.toString()}`),
    );
  }
}
