import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { StocktakesService } from './stocktakes.service';

describe('StocktakesService', () => {
  let service: StocktakesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(StocktakesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list sends the present filters and paging', async () => {
    const promise = service.list({ status: 'Draft', locationId: 'l1', page: 2, pageSize: 10 });
    const req = httpMock.expectOne((r) => r.url === '/api/stocktakes');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('status')).toBe('Draft');
    expect(req.request.params.get('locationId')).toBe('l1');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('10');
    const body = { items: [], page: 2, pageSize: 10, total: 0 };
    req.flush(body);
    expect(await promise).toEqual(body);
  });

  it('list omits absent filters', async () => {
    const promise = service.list({ page: 1, pageSize: 25 });
    const req = httpMock.expectOne((r) => r.url === '/api/stocktakes');
    expect(req.request.params.has('status')).toBe(false);
    expect(req.request.params.has('locationId')).toBe(false);
    req.flush({ items: [], page: 1, pageSize: 25, total: 0 });
    await promise;
  });

  it('get encodes the id', async () => {
    const promise = service.get('a/b');
    const req = httpMock.expectOne('/api/stocktakes/a%2Fb');
    expect(req.request.method).toBe('GET');
    req.flush({});
    await promise;
  });

  it('start posts the location and notes', async () => {
    const promise = service.start({ locationId: 'l1', notes: 'Monthly' });
    const req = httpMock.expectOne('/api/stocktakes');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ locationId: 'l1', notes: 'Monthly' });
    req.flush({});
    await promise;
  });

  it('count puts the quantity to the line URL', async () => {
    const promise = service.count('s1', 'i1', { quantity: 3, unit: 'sack' });
    const req = httpMock.expectOne('/api/stocktakes/s1/lines/i1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ quantity: 3, unit: 'sack' });
    req.flush({});
    await promise;
  });

  it('post and cancel post to their routes', async () => {
    const post = service.post('s1');
    const postReq = httpMock.expectOne('/api/stocktakes/s1/post');
    expect(postReq.request.method).toBe('POST');
    postReq.flush({});
    await post;

    const cancel = service.cancel('s1');
    const cancelReq = httpMock.expectOne('/api/stocktakes/s1/cancel');
    expect(cancelReq.request.method).toBe('POST');
    cancelReq.flush({});
    await cancel;
  });
});
