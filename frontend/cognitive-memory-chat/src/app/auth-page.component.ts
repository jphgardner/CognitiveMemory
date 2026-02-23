import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AppContextService } from './services/app-context.service';
import { AuthService } from './services/auth.service';

@Component({
  selector: 'app-auth-page',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, RouterLink],
  templateUrl: './auth-page.component.html',
})
export class AuthPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly context = inject(AppContextService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly mode = signal<'login' | 'register'>('login');
  readonly email = signal('');
  readonly password = signal('');
  readonly loading = signal(false);
  readonly error = signal('');
  readonly submitLabel = computed(() => (this.mode() === 'register' ? 'Register' : 'Login'));

  ngOnInit(): void {
    const routeMode = (this.route.snapshot.routeConfig?.path ?? '').toLowerCase();
    this.mode.set(routeMode === 'register' ? 'register' : 'login');
    if (this.auth.isAuthenticated) {
      void this.router.navigate(['/portal']);
    }
  }

  async submit(): Promise<void> {
    this.error.set('');
    const email = this.email().trim();
    const password = this.password();
    if (!email || !password) {
      this.error.set('Email and password are required.');
      return;
    }

    this.loading.set(true);
    try {
      if (this.mode() === 'register') {
        await this.auth.register(this.baseUrl(), email, password);
      } else {
        await this.auth.login(this.baseUrl(), email, password);
      }

      this.password.set('');
      await this.router.navigate(['/portal']);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Authentication failed.');
    } finally {
      this.loading.set(false);
    }
  }

  setEmail(value: string): void {
    this.email.set(value);
  }

  setPassword(value: string): void {
    this.password.set(value);
  }

  private baseUrl(): string {
    const trimmed = this.context.apiBase().trim();
    if (!trimmed) {
      return '/api';
    }

    return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
  }
}
