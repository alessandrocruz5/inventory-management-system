import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService, CurrentUser } from './auth.service';

const admin: CurrentUser = {
  id: '1',
  email: 'admin@example.com',
  role: 'Admin',
  tenantName: 'Acme',
};

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;
  let navigate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    navigate = vi.fn().mockResolvedValue(true);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigate } },
      ],
    });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('sets the user when /me returns 200', async () => {
    const promise = service.ensureLoaded();
    httpMock.expectOne('/api/auth/me').flush(admin);
    expect(await promise).toEqual(admin);
    expect(service.user()).toEqual(admin);
  });

  it('resolves null on a 401 and leaves the user unset', async () => {
    const promise = service.ensureLoaded();
    httpMock.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(await promise).toBeNull();
    expect(service.user()).toBeNull();
  });

  it('makes one HTTP request for two ensureLoaded calls', async () => {
    const first = service.ensureLoaded();
    const second = service.ensureLoaded();
    httpMock.expectOne('/api/auth/me').flush(admin);
    expect(await first).toEqual(await second);
  });

  it('login sets the user and isAdmin', async () => {
    const promise = service.login('admin@example.com', 'pw');
    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.body).toEqual({ email: 'admin@example.com', password: 'pw' });
    req.flush(admin);
    await promise;
    expect(service.user()).toEqual(admin);
    expect(service.isAdmin()).toBe(true);
  });

  it('isAdmin is false for staff', async () => {
    const promise = service.login('s@example.com', 'pw');
    httpMock.expectOne('/api/auth/login').flush({ ...admin, role: 'Staff' });
    await promise;
    expect(service.isAdmin()).toBe(false);
  });

  it('login rethrows the HTTP error', async () => {
    const promise = service.login('a@example.com', 'bad');
    httpMock
      .expectOne('/api/auth/login')
      .flush(
        { type: '/problems/invalid-credentials' },
        { status: 401, statusText: 'Unauthorized' },
      );
    await expect(promise).rejects.toMatchObject({ status: 401 });
    expect(service.user()).toBeNull();
  });

  it('logout clears the user and navigates to /login', async () => {
    const login = service.login('admin@example.com', 'pw');
    httpMock.expectOne('/api/auth/login').flush(admin);
    await login;

    const promise = service.logout();
    httpMock.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await promise;
    expect(service.user()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });

  it('logout clears the user even when the post fails', async () => {
    const login = service.login('admin@example.com', 'pw');
    httpMock.expectOne('/api/auth/login').flush(admin);
    await login;

    const promise = service.logout();
    httpMock.expectOne('/api/auth/logout').flush({}, { status: 500, statusText: 'Error' });
    await expect(promise).rejects.toMatchObject({ status: 500 });
    expect(service.user()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login']);
  });
});
