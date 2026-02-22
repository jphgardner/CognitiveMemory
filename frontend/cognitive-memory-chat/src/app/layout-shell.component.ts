import { CommonModule } from '@angular/common';
import { Component, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AppContextService } from './services/app-context.service';
import { AuthService } from './services/auth.service';

@Component({
  selector: 'app-layout-shell',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterOutlet, RouterLink, RouterLinkActive, MatButtonModule, MatIconModule],
  templateUrl: './layout-shell.component.html',
})
export class LayoutShellComponent {
  private readonly router = inject(Router);
  private readonly context = inject(AppContextService);
  private readonly auth = inject(AuthService);

  readonly apiBaseDraft = this.context.apiBase;
  readonly selectedCompanionId = computed(() => this.context.selectedCompanionId());
  readonly user = this.auth.currentUserState;

  saveApiBase(): void {
    this.context.setApiBase(this.apiBaseDraft());
  }

  openWorkspace(): void {
    const id = this.selectedCompanionId();
    if (!id) {
      void this.router.navigate(['/portal']);
      return;
    }

    void this.router.navigate(['/workspace', id]);
  }

  logout(): void {
    this.auth.logout();
    void this.router.navigate(['/auth/login']);
  }
}
