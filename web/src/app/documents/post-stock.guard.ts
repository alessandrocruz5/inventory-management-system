import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { POSTING_TYPES, PostingType } from './documents.service';

export const postStockGuard: CanActivateFn = async (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const type = route.paramMap.get('type');
  if (type === null || !POSTING_TYPES.includes(type as PostingType)) {
    return router.createUrlTree(['/']);
  }
  if (type === 'adjustment' && (await auth.ensureLoaded())?.role !== 'Admin') {
    return router.createUrlTree(['/']);
  }
  return true;
};
