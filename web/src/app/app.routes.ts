import { Routes } from '@angular/router';
import { adminGuard } from './core/auth/admin.guard';
import { authGuard } from './core/auth/auth.guard';
import { postStockGuard } from './documents/post-stock.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./login/login').then((m) => m.Login) },
  { path: '', pathMatch: 'full', redirectTo: 'stock' },
  {
    path: 'stock',
    canActivate: [authGuard],
    loadComponent: () => import('./stock/stock').then((m) => m.Stock),
  },
  {
    path: 'items',
    canActivate: [authGuard],
    loadComponent: () => import('./items/items').then((m) => m.Items),
  },
  {
    path: 'items/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./items/item-detail').then((m) => m.ItemDetail),
  },
  {
    path: 'locations',
    canActivate: [authGuard],
    loadComponent: () => import('./locations/locations').then((m) => m.Locations),
  },
  {
    path: 'documents',
    canActivate: [authGuard],
    loadComponent: () => import('./documents/documents').then((m) => m.Documents),
  },
  {
    path: 'documents/new/:type',
    canActivate: [authGuard, postStockGuard],
    loadComponent: () => import('./documents/post-stock').then((m) => m.PostStock),
  },
  {
    path: 'documents/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./documents/document-detail').then((m) => m.DocumentDetail),
  },
  {
    path: 'stocktakes',
    canActivate: [authGuard],
    loadComponent: () => import('./stocktakes/stocktakes').then((m) => m.Stocktakes),
  },
  {
    path: 'stocktakes/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./stocktakes/stocktake-detail').then((m) => m.StocktakeDetail),
  },
  {
    path: 'admin/ledger',
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./ledger/ledger-health').then((m) => m.LedgerHealth),
  },
  { path: '**', redirectTo: '' },
];
