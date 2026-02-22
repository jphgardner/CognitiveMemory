import { CanMatchFn, Routes } from '@angular/router';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './services/auth.service';
import { AuthPageComponent } from './auth-page.component';
import { BlankLayoutComponent } from './blank-layout.component';
import { ConsoleLayoutComponent } from './console-layout.component';
import { LayoutShellComponent } from './layout-shell.component';
import { ClientPortalComponent } from './portal/client-portal.component';
import { WorkspaceComponent } from './workspace.component';

const validPages = new Set(['overview', 'chat', 'memory', 'debates', 'analytics', 'operations']);
const canMatchConsolePage: CanMatchFn = (_, segments) => {
  const page = (segments[0]?.path ?? '').toLowerCase();
  return validPages.has(page);
};

const canMatchAuthenticated: CanMatchFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isAuthenticated ? true : router.parseUrl('/auth/login');
};

const canMatchGuestOnly: CanMatchFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.isAuthenticated ? router.parseUrl('/portal') : true;
};

export const routes: Routes = [
  {
    path: 'auth',
    component: BlankLayoutComponent,
    canMatch: [canMatchGuestOnly],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'login' },
      { path: 'login', component: AuthPageComponent },
      { path: 'register', component: AuthPageComponent },
    ],
  },
  {
    path: '',
    component: LayoutShellComponent,
    canMatch: [canMatchAuthenticated],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'portal' },
      { path: 'portal', component: ClientPortalComponent },
      { path: 'portal/:companionId', component: ClientPortalComponent },
      { path: 'workspace', component: WorkspaceComponent },
      { path: 'workspace/:companionId', component: WorkspaceComponent },
      {
        path: 'console',
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'overview' },
          { path: ':page', component: ConsoleLayoutComponent, canMatch: [canMatchConsolePage] },
        ],
      },
    ],
  },
  { path: '**', redirectTo: 'auth/login' },
];
