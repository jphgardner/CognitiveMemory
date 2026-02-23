import { Injectable } from '@angular/core';
import { ChatTurn } from '../models/console.models';

@Injectable({ providedIn: 'root' })
export class ChatStateService {
  assistantTurnCount(turns: ChatTurn[]): number {
    return turns.filter((x) => x.role === 'assistant').length;
  }

  totalToolCalls(turns: ChatTurn[]): number {
    let total = 0;
    for (const turn of turns) {
      if (turn.role !== 'assistant') {
        continue;
      }

      total += turn.toolCalls?.length ?? 0;
    }

    return total;
  }

  successfulToolCalls(turns: ChatTurn[]): number {
    let total = 0;
    for (const turn of turns) {
      if (turn.role !== 'assistant') {
        continue;
      }

      for (const call of turn.toolCalls ?? []) {
        if (call.succeeded) {
          total += 1;
        }
      }
    }

    return total;
  }

  averageResponseSeconds(turns: ChatTurn[]): number {
    const samples: number[] = [];
    for (const turn of turns) {
      if (turn.role !== 'assistant' || !turn.startedAtUtc || !turn.completedAtUtc) {
        continue;
      }

      const start = new Date(turn.startedAtUtc).getTime();
      const end = new Date(turn.completedAtUtc).getTime();
      if (Number.isFinite(start) && Number.isFinite(end) && end >= start) {
        samples.push((end - start) / 1000);
      }
    }

    if (samples.length === 0) {
      return 0;
    }

    const total = samples.reduce((sum, value) => sum + value, 0);
    return Number((total / samples.length).toFixed(2));
  }

  responseThroughputPerMinute(turns: ChatTurn[]): number {
    const assistantTurns = turns.filter((x) => x.role === 'assistant');
    if (assistantTurns.length < 2) {
      return assistantTurns.length;
    }

    const timestamps = assistantTurns
      .map((x) => new Date(x.completedAtUtc ?? x.startedAtUtc ?? '').getTime())
      .filter((x) => Number.isFinite(x))
      .sort((a, b) => a - b);

    if (timestamps.length < 2) {
      return assistantTurns.length;
    }

    const elapsedMinutes = Math.max((timestamps[timestamps.length - 1] - timestamps[0]) / 60_000, 1);
    return Number((assistantTurns.length / elapsedMinutes).toFixed(2));
  }

  memoryLayerMetrics(turns: ChatTurn[]): Array<{ layer: string; count: number; percent: number }> {
    const counts = new Map<string, number>();

    for (const turn of turns) {
      if (turn.role !== 'assistant') {
        continue;
      }

      for (const layer of turn.memoryLayers ?? []) {
        counts.set(layer, (counts.get(layer) ?? 0) + 1);
      }
    }

    const entries = Array.from(counts.entries())
      .map(([layer, count]) => ({ layer, count }))
      .sort((a, b) => b.count - a.count);

    const total = entries.reduce((sum, item) => sum + item.count, 0);
    if (total === 0) {
      return [];
    }

    return entries.map((item) => ({
      layer: item.layer,
      count: item.count,
      percent: Math.round((item.count / total) * 100),
    }));
  }

  toolOutcomeMetrics(turns: ChatTurn[]): Array<{ label: string; count: number; percent: number }> {
    const total = this.totalToolCalls(turns);
    const success = this.successfulToolCalls(turns);
    const failed = Math.max(total - success, 0);
    if (total === 0) {
      return [];
    }

    return [
      { label: 'Successful', count: success, percent: Math.round((success / total) * 100) },
      { label: 'Failed', count: failed, percent: Math.round((failed / total) * 100) },
    ];
  }
}
