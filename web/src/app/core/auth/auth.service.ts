import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

export interface CurrentUser {
  id: string;
  email: string;
  role: 'Admin' | 'Staff';
  tenantName: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly _user = signal<CurrentUser | null>(null);
  readonly user = this._user.asReadonly();
  readonly isAdmin = computed(() => this._user()?.role === 'Admin');

  private loaded: Promise<CurrentUser | null> | null = null;

  ensureLoaded(): Promise<CurrentUser | null> {
    this.loaded ??= this.loadCurrentUser();
    return this.loaded;
  }

  async login(email: string, password: string): Promise<CurrentUser> {
    this.loaded = null;
    const user = await firstValueFrom(
      this.http.post<CurrentUser>('/api/auth/login', { email, password }),
    );
    this._user.set(user);
    return user;
  }

  async logout(): Promise<void> {
    this.loaded = null;
    try {
      await firstValueFrom(this.http.post<void>('/api/auth/logout', null));
    } finally {
      this._user.set(null);
      await this.router.navigate(['/login']);
    }
  }

  private async loadCurrentUser(): Promise<CurrentUser | null> {
    try {
      const user = await firstValueFrom(this.http.get<CurrentUser>('/api/auth/me'));
      this._user.set(user);
      return user;
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this._user.set(null);
        return null;
      }
      throw error;
    }
  }
}
