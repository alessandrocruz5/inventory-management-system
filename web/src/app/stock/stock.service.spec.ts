import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { StockPage, StockRow, StockService } from './stock.service';

const row: StockRow = {
  itemId: 'i1',
  sku: 'RICE-1',
  itemName: 'Rice',
  baseUnit: 'kg',
  locationId: 'l1',
  locationCode: 'MAIN',
  locationName: 'Main',
  onHand: 12.5,
  stockValue: 500,
  averageCost: 40,
  lastUnitCost: 41,
};

describe('StockService', () => {
  let service: StockService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(StockService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list sends all present params', async () => {
    const promise = service.list({ itemId: 'i1', locationId: 'l1', page: 2, pageSize: 25 });
    const req = httpMock.expectOne((r) => r.url === '/api/stock');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('itemId')).toBe('i1');
    expect(req.request.params.get('locationId')).toBe('l1');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('25');
    const body: StockPage = {
      items: [row],
      page: 2,
      pageSize: 25,
      total: 1,
      totals: { stockValue: 500 },
    };
    req.flush(body);
    expect(await promise).toEqual(body);
  });

  it('list omits undefined filters', async () => {
    const promise = service.list({ page: 1, pageSize: 10 });
    const req = httpMock.expectOne((r) => r.url === '/api/stock');
    expect(req.request.params.has('itemId')).toBe(false);
    expect(req.request.params.has('locationId')).toBe(false);
    req.flush({ items: [], page: 1, pageSize: 10, total: 0, totals: { stockValue: 0 } });
    await promise;
  });

  it('onHand returns the first row quantity', async () => {
    const promise = service.onHand('i1', 'l1');
    const req = httpMock.expectOne((r) => r.url === '/api/stock');
    expect(req.request.params.get('itemId')).toBe('i1');
    expect(req.request.params.get('locationId')).toBe('l1');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('1');
    req.flush({ items: [row], page: 1, pageSize: 1, total: 1, totals: { stockValue: 500 } });
    expect(await promise).toBe(12.5);
  });

  it('onHand returns 0 for an empty page', async () => {
    const promise = service.onHand('i1', 'l1');
    httpMock
      .expectOne((r) => r.url === '/api/stock')
      .flush({ items: [], page: 1, pageSize: 1, total: 0, totals: { stockValue: 0 } });
    expect(await promise).toBe(0);
  });
});
