import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ApiUrlService {
  normalize(base: string): string {
    const trimmed = (base ?? '').trim();
    if (!trimmed) {
      return '/api';
    }

    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
  }
}
