import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  UrlTree,
  convertToParamMap,
  provideRouter,
} from '@angular/router';
import { AuthService, CurrentUser } from '../core/auth/auth.service';
import { postStockGuard } from './post-stock.guard';

describe('postStockGuard', () => {
  const ensureLoaded = vi.fn();

  beforeEach(() => {
    ensureLoaded.mockReset();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { ensureLoaded } }],
    });
  });

  function run(type: string): Promise<boolean | UrlTree> {
    const route = { paramMap: convertToParamMap({ type }) } as ActivatedRouteSnapshot;
    return TestBed.runInInjectionContext(
      () => postStockGuard(route, {} as never) as Promise<boolean | UrlTree>,
    );
  }

  const user = (role: CurrentUser['role']) => ({ id: 'u', email: 'e', role, tenantName: 't' });

  it('redirects an unknown type to /', async () => {
    const result = await run('bogus');
    expect(result).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  it('redirects Staff away from adjustment', async () => {
    ensureLoaded.mockResolvedValue(user('Staff'));
    const result = await run('adjustment');
    expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/');
  });

  it('lets Admin into adjustment', async () => {
    ensureLoaded.mockResolvedValue(user('Admin'));
    expect(await run('adjustment')).toBe(true);
  });

  it('lets Staff into receipt', async () => {
    ensureLoaded.mockResolvedValue(user('Staff'));
    expect(await run('receipt')).toBe(true);
  });
});
