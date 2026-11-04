import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  Router,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { authGuard } from './auth.guard';

describe('authGuard', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function runGuard(): Promise<boolean | UrlTree> {
    return TestBed.runInInjectionContext(
      () =>
        authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot) as Promise<
          boolean | UrlTree
        >,
    );
  }

  it('returns true when the user loads', async () => {
    const result = runGuard();
    httpMock.expectOne('/api/auth/me').flush({
      id: '1',
      email: 'a@example.com',
      role: 'Staff',
      tenantName: 'Acme',
    });
    expect(await result).toBe(true);
  });

  it('returns a UrlTree for /login on 401', async () => {
    const result = runGuard();
    httpMock.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    const tree = await result;
    expect(tree).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(tree as UrlTree)).toBe('/login');
  });
});
