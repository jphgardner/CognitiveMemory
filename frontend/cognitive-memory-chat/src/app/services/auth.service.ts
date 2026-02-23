import { HttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface AuthUser {
  userId: string;
  email: string;
  createdAtUtc: string;
  updatedAtUtc: string;
}

interface AuthResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: AuthUser;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly tokenKey = 'cm.auth.token.v1';
  private readonly userKey = 'cm.auth.user.v1';
  readonly tokenState = signal<string | null>(this.loadToken());
  readonly currentUserState = signal<AuthUser | null>(this.loadUser());

  get token(): string | null {
    return this.tokenState();
  }

  get isAuthenticated(): boolean {
    return this.tokenState() !== null;
  }

  get currentUser(): AuthUser | null {
    return this.currentUserState();
  }

  async register(baseUrl: string, email: string, password: string): Promise<AuthUser> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${this.baseUrl(baseUrl)}/auth/register`, {
        email: email.trim(),
        password,
      }),
    );
    this.persistAuth(response);
    return response.user;
  }

  async login(baseUrl: string, email: string, password: string): Promise<AuthUser> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>(`${this.baseUrl(baseUrl)}/auth/login`, {
        email: email.trim(),
        password,
      }),
    );
    this.persistAuth(response);
    return response.user;
  }

  async loadMe(baseUrl: string): Promise<AuthUser | null> {
    if (!this.token) {
      return null;
    }

    try {
      const me = await firstValueFrom(this.http.get<AuthUser>(`${this.baseUrl(baseUrl)}/auth/me`));
      localStorage.setItem(this.userKey, JSON.stringify(me));
      this.currentUserState.set(me);
      return me;
    } catch (error) {
      if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
        this.logout();
        return null;
      }

      return this.currentUser;
    }
  }

  logout(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.userKey);
    this.tokenState.set(null);
    this.currentUserState.set(null);
  }

  private persistAuth(response: AuthResponse): void {
    localStorage.setItem(this.tokenKey, response.accessToken);
    localStorage.setItem(this.userKey, JSON.stringify(response.user));
    this.tokenState.set(response.accessToken);
    this.currentUserState.set(response.user);
  }

  private loadToken(): string | null {
    const value = localStorage.getItem(this.tokenKey)?.trim() ?? '';
    return value.length > 0 ? value : null;
  }

  private loadUser(): AuthUser | null {
    const raw = localStorage.getItem(this.userKey);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as AuthUser;
    } catch {
      return null;
    }
  }

  private baseUrl(value: string): string {
    const trimmed = value.trim();
    if (!trimmed) {
      return '/api';
    }

    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
  }
}
