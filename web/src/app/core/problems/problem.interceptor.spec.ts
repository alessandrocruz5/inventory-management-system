import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import {
  DEFAULT_PROBLEM_MESSAGE,
  PROBLEM_MESSAGES,
  problemMessage,
  problemSlug,
} from './problem-messages';
import { problemInterceptor } from './problem.interceptor';

describe('problemInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let navigate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    navigate = vi.fn().mockResolvedValue(true);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([problemInterceptor])),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigate } },
      ],
    });
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function fail(url: string, status: number): Promise<unknown> {
    const result = new Promise<unknown>((resolve) =>
      http.get(url).subscribe({ error: (error: unknown) => resolve(error) }),
    );
    httpMock.expectOne(url).flush({}, { status, statusText: 'error' });
    return result;
  }

  it('navigates to /login on a 401 from another endpoint and rethrows', async () => {
    const error = await fail('/api/items', 401);
    expect(navigate).toHaveBeenCalledWith(['/login']);
    expect((error as { status: number }).status).toBe(401);
  });

  it('does not navigate on a 401 from /api/auth/login', async () => {
    await fail('/api/auth/login', 401);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not navigate on a 401 from /api/auth/me', async () => {
    await fail('/api/auth/me', 401);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not navigate on a 500 and rethrows', async () => {
    const error = await fail('/api/items', 500);
    expect(navigate).not.toHaveBeenCalled();
    expect((error as { status: number }).status).toBe(500);
  });
});

describe('problem messages', () => {
  it('extracts the slug from a problem type', () => {
    expect(problemSlug('/problems/validation')).toBe('validation');
    expect(problemSlug('/other/validation')).toBeNull();
    expect(problemSlug(42)).toBeNull();
  });

  it('maps a known slug to its message', () => {
    expect(problemMessage({ type: '/problems/invalid-credentials' })).toBe(
      PROBLEM_MESSAGES['invalid-credentials'],
    );
  });

  it('falls back to the default for an unknown slug', () => {
    expect(problemMessage({ type: '/problems/nope' })).toBe(DEFAULT_PROBLEM_MESSAGE);
  });

  it('falls back to the default for a non-object body', () => {
    expect(problemMessage('boom')).toBe(DEFAULT_PROBLEM_MESSAGE);
    expect(problemMessage(null)).toBe(DEFAULT_PROBLEM_MESSAGE);
  });
});
