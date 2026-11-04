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
  const logout = vi.fn();

  beforeEach(async () => {
    user.set(null);
    logout.mockReset();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: AuthService, useValue: { user, logout } }],
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

  it('shows no toolbar when signed out', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-toolbar')).toBeNull();
  });
});
