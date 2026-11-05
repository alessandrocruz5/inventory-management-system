import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./login/login').then((m) => m.Login) },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./home/home').then((m) => m.Home),
  },
  {
    path: 'items',
    canActivate: [authGuard],
    loadComponent: () => import('./items/items').then((m) => m.Items),
  },
  {
    path: 'locations',
    canActivate: [authGuard],
    loadComponent: () => import('./locations/locations').then((m) => m.Locations),
  },
  { path: '**', redirectTo: '' },
];
