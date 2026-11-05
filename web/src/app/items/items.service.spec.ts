import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Item, ItemsService } from './items.service';

const item: Item = {
  id: 'a1',
  sku: 'RICE-1',
  name: 'Rice',
  baseUnit: 'kg',
  isActive: true,
  version: 'AAAAAAAAB9E=',
};

describe('ItemsService', () => {
  let service: ItemsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ItemsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list sends trimmed search, active, page and pageSize', async () => {
    const promise = service.list({ search: ' rice ', active: true, page: 2, pageSize: 25 });
    const req = httpMock.expectOne((r) => r.url === '/api/items');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('search')).toBe('rice');
    expect(req.request.params.get('active')).toBe('true');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('25');
    const body = { items: [item], page: 2, pageSize: 25, total: 1 };
    req.flush(body);
    expect(await promise).toEqual(body);
  });

  it('list omits a blank search and an undefined active', async () => {
    const promise = service.list({ search: '   ', page: 1, pageSize: 10 });
    const req = httpMock.expectOne((r) => r.url === '/api/items');
    expect(req.request.params.has('search')).toBe(false);
    expect(req.request.params.has('active')).toBe(false);
    expect(req.request.params.get('page')).toBe('1');
    req.flush({ items: [], page: 1, pageSize: 10, total: 0 });
    await promise;
  });

  it('create posts the input', async () => {
    const promise = service.create({ sku: 'RICE-1', name: 'Rice', baseUnit: 'kg' });
    const req = httpMock.expectOne('/api/items');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ sku: 'RICE-1', name: 'Rice', baseUnit: 'kg' });
    req.flush(item);
    expect(await promise).toEqual(item);
  });

  it('update sends the item version', async () => {
    const promise = service.update(item, { sku: 'RICE-2', name: 'Rice 2', baseUnit: 'kg' });
    const req = httpMock.expectOne('/api/items/a1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      sku: 'RICE-2',
      name: 'Rice 2',
      baseUnit: 'kg',
      version: item.version,
    });
    req.flush(item);
    await promise;
  });

  it('deactivate is a POST to /deactivate', async () => {
    const promise = service.deactivate('a1');
    const req = httpMock.expectOne('/api/items/a1/deactivate');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ ...item, isActive: false });
    expect((await promise).isActive).toBe(false);
  });

  it('get encodes the id', async () => {
    const promise = service.get('a/b');
    httpMock.expectOne('/api/items/a%2Fb').flush(item);
    await promise;
  });
});
