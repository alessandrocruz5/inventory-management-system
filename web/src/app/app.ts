import { Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthService } from './core/auth/auth.service';
import { injectIsNarrow } from './shared/layout';

/** `/documents` and `/documents/:id` are "Documents"; `/documents/new/*` belongs to Post stock (plan D18). */
const DOCUMENTS_ACTIVE = /^\/documents(\/(?!new(\/|$))[^/?#]+)?([?#].*)?$/;

export function isDocumentsUrl(url: string): boolean {
  return DOCUMENTS_ACTIVE.test(url);
}

/** First letters of the first two `.`/`_`/`-` segments of the local part, else the first two characters. */
export function initialsOf(email: string): string {
  const local = email.split('@')[0] ?? '';
  const parts = local.split(/[._-]/).filter((p) => p.length > 0);
  const text = parts.length >= 2 ? parts[0][0] + parts[1][0] : email.slice(0, 2);
  return text.toUpperCase();
}

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatButtonModule, MatIconModule],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
  host: { '(document:keydown.escape)': 'closeDrawer()' },
})
export class App {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly isNarrow = injectIsNarrow();
  protected readonly drawerOpen = signal(false);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );
  protected readonly documentsActive = computed(() => isDocumentsUrl(this.url()));
  protected readonly initials = computed(() => initialsOf(this.auth.user()?.email ?? ''));

  constructor() {
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.closeDrawer());
    effect(() => {
      if (!this.isNarrow()) {
        this.drawerOpen.set(false);
      }
    });
  }

  protected openDrawer(): void {
    this.drawerOpen.set(true);
  }

  protected closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  protected signOut(): void {
    void this.auth.logout();
  }
}
