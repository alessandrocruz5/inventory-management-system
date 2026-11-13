import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  DocumentsService,
  POSTING_ROUTES,
  POSTING_TYPES,
  PostingBody,
  PostingType,
} from './documents.service';

describe('DocumentsService', () => {
  let service: DocumentsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DocumentsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list sends all present params', async () => {
    const promise = service.list({
      type: 'Receipt',
      postedFrom: '2026-10-29T16:00:00.000Z',
      postedTo: '2026-10-30T16:00:00.000Z',
      page: 3,
      pageSize: 20,
    });
    const req = httpMock.expectOne((r) => r.url === '/api/documents');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('type')).toBe('Receipt');
    expect(req.request.params.get('postedFrom')).toBe('2026-10-29T16:00:00.000Z');
    expect(req.request.params.get('postedTo')).toBe('2026-10-30T16:00:00.000Z');
    expect(req.request.params.get('page')).toBe('3');
    expect(req.request.params.get('pageSize')).toBe('20');
    const body = { items: [], page: 3, pageSize: 20, total: 0 };
    req.flush(body);
    expect(await promise).toEqual(body);
  });

  it('list omits undefined filters', async () => {
    const promise = service.list({ page: 1, pageSize: 10 });
    const req = httpMock.expectOne((r) => r.url === '/api/documents');
    expect(req.request.params.has('type')).toBe(false);
    expect(req.request.params.has('postedFrom')).toBe(false);
    expect(req.request.params.has('postedTo')).toBe(false);
    req.flush({ items: [], page: 1, pageSize: 10, total: 0 });
    await promise;
  });

  it('get encodes the id', async () => {
    const promise = service.get('a/b');
    const req = httpMock.expectOne('/api/documents/a%2Fb');
    expect(req.request.method).toBe('GET');
    const body = { id: 'a/b', lines: [] };
    req.flush(body);
    expect(await promise).toEqual(body as never);
  });

  const bodies: Record<PostingType, PostingBody> = {
    receipt: {
      locationId: 'l1',
      reference: 'PO-1',
      notes: null,
      lines: [{ itemId: 'i1', quantity: 2, unitCost: 10.5 }],
    },
    issue: {
      locationId: 'l1',
      reference: null,
      notes: 'n',
      lines: [{ itemId: 'i1', quantity: 1 }],
    },
    transfer: {
      fromLocationId: 'l1',
      toLocationId: 'l2',
      reference: null,
      notes: null,
      lines: [{ itemId: 'i1', quantity: 3 }],
    },
    adjustment: {
      locationId: 'l1',
      reasonCode: 'Found',
      reference: null,
      notes: null,
      lines: [{ itemId: 'i1', quantityDelta: -1, unitCost: null }],
    },
  };

  for (const type of POSTING_TYPES) {
    it(`post sends ${type} body and Idempotency-Key`, async () => {
      const promise = service.post(type, 'key-1', bodies[type]);
      const req = httpMock.expectOne(POSTING_ROUTES[type]);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(bodies[type]);
      expect(req.request.headers.get('Idempotency-Key')).toBe('key-1');
      req.flush({ id: 'd1', type: 'Receipt' }, { status: 200, statusText: 'OK' });
      expect(await promise).toEqual({ id: 'd1', type: 'Receipt' });
    });
  }

  it('routes are the documented ones', () => {
    expect(POSTING_ROUTES).toEqual({
      receipt: '/api/documents/receipts',
      issue: '/api/documents/issues',
      transfer: '/api/documents/transfers',
      adjustment: '/api/documents/adjustments',
    });
  });
});
