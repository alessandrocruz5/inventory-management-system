import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { AuthService, CurrentUser } from './auth.service';
import { adminGuard } from './admin.guard';

describe('adminGuard', () => {
  const ensureLoaded = vi.fn();

  beforeEach(() => {
    ensureLoaded.mockReset();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { ensureLoaded } }],
    });
  });

  function run(): Promise<boolean | UrlTree> {
    return TestBed.runInInjectionContext(
      () => adminGuard({} as never, {} as never) as Promise<boolean | UrlTree>,
    );
  }

  const user = (role: CurrentUser['role']) => ({ id: 'u', email: 'e', role, tenantName: 't' });

  it('lets Admin through', async () => {
    ensureLoaded.mockResolvedValue(user('Admin'));
    expect(await run()).toBe(true);
  });

  it('redirects Staff to /', async () => {
    ensureLoaded.mockResolvedValue(user('Staff'));
    const result = await run();
    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  it('redirects a signed-out user to /', async () => {
    ensureLoaded.mockResolvedValue(null);
    const result = await run();
    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });
});
