import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

// 401s from these endpoints are handled by the caller and the guard.
const AUTH_HANDLED_URLS = ['/api/auth/login', '/api/auth/me'];

export const problemInterceptor: HttpInterceptorFn = (req, next) => {
  const router = inject(Router);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !AUTH_HANDLED_URLS.some((url) => req.url.split('?')[0] === url)
      ) {
        void router.navigate(['/login']);
      }
      return throwError(() => error);
    }),
  );
};
