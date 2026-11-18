import { BreakpointObserver } from '@angular/cdk/layout';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { App, initialsOf, isDocumentsUrl } from './app';
import { AuthService, CurrentUser } from './core/auth/auth.service';

const staff: CurrentUser = {
  id: 'u1',
  email: 'staff@demo.test',
  role: 'Staff',
  tenantName: 'Demo Retail',
};

describe('App', () => {
  const user = signal<CurrentUser | null>(null);
  const isAdmin = signal(false);
  const logout = vi.fn();

  async function setup(narrow = false): Promise<void> {
    user.set(null);
    isAdmin.set(false);
    logout.mockReset();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([{ path: '**', children: [] }]),
        { provide: AuthService, useValue: { user, isAdmin, logout } },
        ...(narrow
          ? [
              {
                provide: BreakpointObserver,
                useValue: {
                  observe: () => of({ matches: true, breakpoints: {} }),
                  isMatched: () => true,
                },
              },
            ]
          : []),
      ],
    }).compileComponents();
  }

  async function render(narrow = false, signedIn = true) {
    await setup(narrow);
    if (signedIn) {
      user.set(staff);
    }
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('should create the app', async () => {
    await setup();
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('shows the tenant, the user and the role in the sidebar when signed in', async () => {
    const { el } = await render();
    const aside = el.querySelector('aside');
    expect(aside).not.toBeNull();
    expect(aside?.textContent).toContain('Demo Retail');
    expect(aside?.textContent).toContain('staff@demo.test');
    expect(aside?.textContent).toContain('Staff');
    expect(aside?.textContent).toContain('ST');
  });

  it('signs out from the icon button', async () => {
    const { el } = await render();
    const button = el.querySelector<HTMLButtonElement>('button[aria-label="Sign out"]');
    expect(button).not.toBeNull();
    button?.click();
    expect(logout).toHaveBeenCalledOnce();
  });

  it('links to the Inventory and Post stock screens', async () => {
    const { el } = await render();
    const expected: [string, string][] = [
      ['/stock', 'Stock'],
      ['/items', 'Items'],
      ['/locations', 'Locations'],
      ['/documents', 'Documents'],
      ['/documents/new/receipt', 'Receive stock'],
      ['/documents/new/issue', 'Use or sell stock'],
      ['/documents/new/transfer', 'Move stock'],
    ];
    for (const [href, text] of expected) {
      const link = el.querySelector(`aside a[href="${href}"]`);
      expect(link, href).not.toBeNull();
      expect(link?.textContent).toContain(text);
    }
  });

  it('shows Correct stock to Admin only', async () => {
    const { fixture, el } = await render();
    expect(el.querySelector('a[href="/documents/new/adjustment"]')).toBeNull();
    isAdmin.set(true);
    await fixture.whenStable();
    const link = el.querySelector('a[href="/documents/new/adjustment"]');
    expect(link).not.toBeNull();
    expect(link?.textContent).toContain('Correct stock');
  });

  it('shows Ledger health to Admin only', async () => {
    const { fixture, el } = await render();
    expect(el.querySelector('a[href="/admin/ledger"]')).toBeNull();
    isAdmin.set(true);
    await fixture.whenStable();
    const link = el.querySelector('a[href="/admin/ledger"]');
    expect(link).not.toBeNull();
    expect(link?.textContent).toContain('Ledger health');
  });

  it('shows no sidebar or top bar when signed out', async () => {
    const { el } = await render(false, false);
    expect(el.querySelector('aside')).toBeNull();
    expect(el.querySelector('header')).toBeNull();
    expect(el.querySelector('main')).not.toBeNull();
  });

  describe('narrow viewport', () => {
    it('shows the top bar and opens a single drawer from the menu button', async () => {
      const { fixture, el } = await render(true);
      const bar = el.querySelector('header.topbar');
      expect(bar).not.toBeNull();
      const post = bar?.querySelector('a[href="/documents/new/receipt"]');
      expect(post).not.toBeNull();
      expect(post?.tagName).toBe('A');
      expect(post?.textContent).toContain('Post');
      expect(bar?.querySelector('button')?.getAttribute('aria-label')).toBe('Open navigation');
      expect(el.querySelectorAll('aside')).toHaveLength(0);
      expect(el.querySelector('.backdrop')).toBeNull();

      el.querySelector<HTMLButtonElement>('button[aria-label="Open navigation"]')?.click();
      await fixture.whenStable();
      expect(el.querySelectorAll('aside')).toHaveLength(1);
      expect(el.querySelectorAll('a[href="/stock"]')).toHaveLength(1);
      expect(el.querySelector('.backdrop')).not.toBeNull();
    });

    it('closes the drawer on a backdrop click and on Escape', async () => {
      const { fixture, el } = await render(true);
      const open = async () => {
        el.querySelector<HTMLButtonElement>('button[aria-label="Open navigation"]')?.click();
        await fixture.whenStable();
      };
      await open();
      el.querySelector<HTMLButtonElement>('.backdrop')?.click();
      await fixture.whenStable();
      expect(el.querySelectorAll('aside')).toHaveLength(0);

      await open();
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      await fixture.whenStable();
      expect(el.querySelectorAll('aside')).toHaveLength(0);
    });

    it('closes the drawer after navigation', async () => {
      const { fixture, el } = await render(true);
      el.querySelector<HTMLButtonElement>('button[aria-label="Open navigation"]')?.click();
      await fixture.whenStable();
      expect(el.querySelectorAll('aside')).toHaveLength(1);
      await TestBed.inject(Router).navigateByUrl('/items');
      await fixture.whenStable();
      expect(el.querySelectorAll('aside')).toHaveLength(0);
    });
  });

  describe('Documents active state', () => {
    it('is active on /documents/:id but not on /documents/new/*', async () => {
      const { fixture, el } = await render();
      const router = TestBed.inject(Router);
      const documents = () => el.querySelector('aside a[href="/documents"]');

      await router.navigateByUrl('/documents/abc');
      await fixture.whenStable();
      expect(documents()?.classList.contains('active')).toBe(true);
      expect(documents()?.getAttribute('aria-current')).toBe('page');

      await router.navigateByUrl('/documents/new/receipt');
      await fixture.whenStable();
      expect(documents()?.classList.contains('active')).toBe(false);
      expect(documents()?.hasAttribute('aria-current')).toBe(false);
      expect(
        el.querySelector('aside a[href="/documents/new/receipt"]')?.classList.contains('active'),
      ).toBe(true);
    });

    it('matches the documents URL shapes', () => {
      expect(isDocumentsUrl('/documents')).toBe(true);
      expect(isDocumentsUrl('/documents?type=Receipt')).toBe(true);
      expect(isDocumentsUrl('/documents/abc')).toBe(true);
      expect(isDocumentsUrl('/documents/new')).toBe(false);
      expect(isDocumentsUrl('/documents/new/receipt')).toBe(false);
      expect(isDocumentsUrl('/items')).toBe(false);
    });
  });

  describe('initialsOf', () => {
    it('uses the first two local-part segments, else the first two characters', () => {
      expect(initialsOf('ana.reyes@lumen.ph')).toBe('AR');
      expect(initialsOf('juan_dela-cruz@x.ph')).toBe('JD');
      expect(initialsOf('staff@demo.test')).toBe('ST');
      expect(initialsOf('')).toBe('');
    });
  });
});
