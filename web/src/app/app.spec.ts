import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { App } from './app';
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

  beforeEach(async () => {
    user.set(null);
    isAdmin.set(false);
    logout.mockReset();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: AuthService, useValue: { user, isAdmin, logout } }],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('shows the tenant, the user and Sign out when signed in', async () => {
    user.set(staff);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const toolbar = el.querySelector('mat-toolbar');
    expect(toolbar).not.toBeNull();
    expect(toolbar?.textContent).toContain('Demo Retail');
    expect(toolbar?.textContent).toContain('staff@demo.test (Staff)');
    const button = el.querySelector('button');
    expect(button?.textContent).toContain('Sign out');
    button?.click();
    expect(logout).toHaveBeenCalledOnce();
  });

  it('links to Items and Locations in the toolbar when signed in', async () => {
    user.set(staff);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const items = el.querySelector('a[href="/items"]');
    expect(items).not.toBeNull();
    expect(items?.textContent).toContain('Items');
    const locations = el.querySelector('a[href="/locations"]');
    expect(locations).not.toBeNull();
    expect(locations?.textContent).toContain('Locations');
  });

  it('links to the stock screens in the toolbar', async () => {
    user.set(staff);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const expected: [string, string][] = [
      ['/stock', 'Stock'],
      ['/documents/new/receipt', 'Receive stock'],
      ['/documents/new/issue', 'Use or sell stock'],
      ['/documents/new/transfer', 'Move stock'],
      ['/documents', 'Documents'],
    ];
    for (const [href, text] of expected) {
      const link = el.querySelector(`a[href="${href}"]`);
      expect(link, href).not.toBeNull();
      expect(link?.textContent).toContain(text);
    }
  });

  it('shows Correct stock to Admin only', async () => {
    user.set(staff);
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('a[href="/documents/new/adjustment"]')).toBeNull();
    isAdmin.set(true);
    await fixture.whenStable();
    const link = el.querySelector('a[href="/documents/new/adjustment"]');
    expect(link).not.toBeNull();
    expect(link?.textContent).toContain('Correct stock');
  });

  it('shows no toolbar when signed out', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-toolbar')).toBeNull();
  });
});
