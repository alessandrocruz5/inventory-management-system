import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LedgerHealth, LedgerRebuild, LedgerService } from './ledger.service';

describe('LedgerService', () => {
  let service: LedgerService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LedgerService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('health GETs /api/ledger/health and resolves the body', async () => {
    const body: LedgerHealth = { checkedAt: '2026-11-16T00:00:00Z', healthy: true, invariants: [] };
    const promise = service.health();
    const req = httpMock.expectOne('/api/ledger/health');
    expect(req.request.method).toBe('GET');
    req.flush(body);
    expect(await promise).toEqual(body);
  });

  it('rebuild POSTs /api/ledger/rebuild with a null body and no Idempotency-Key', async () => {
    const body: LedgerRebuild = {
      rebuiltAt: '2026-11-16T00:00:00Z',
      balancesChecked: 3,
      differences: [],
    };
    const promise = service.rebuild();
    const req = httpMock.expectOne('/api/ledger/rebuild');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    expect(req.request.headers.has('Idempotency-Key')).toBe(false);
    req.flush(body);
    expect(await promise).toEqual(body);
  });
});
