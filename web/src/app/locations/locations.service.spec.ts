import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Location, LocationsService } from './locations.service';

const location: Location = {
  id: 'l1',
  code: 'QC',
  name: 'Quezon City',
  kind: 'Physical',
  isSystem: false,
  allowNegativeStock: false,
  isActive: true,
  version: 'AAAAAAAAB9E=',
};

describe('LocationsService', () => {
  let service: LocationsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LocationsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list sends page and pageSize', async () => {
    const promise = service.list({ page: 2, pageSize: 50 });
    const req = httpMock.expectOne((r) => r.url === '/api/locations');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('50');
    const body = { items: [location], page: 2, pageSize: 50, total: 1 };
    req.flush(body);
    expect(await promise).toEqual(body);
  });

  it('create posts the input', async () => {
    const promise = service.create({ code: 'QC', name: 'Quezon City' });
    const req = httpMock.expectOne('/api/locations');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'QC', name: 'Quezon City' });
    req.flush(location);
    expect(await promise).toEqual(location);
  });

  it('update sends the location version', async () => {
    const promise = service.update(location, { code: 'QC2', name: 'Quezon City 2' });
    const req = httpMock.expectOne('/api/locations/l1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({
      code: 'QC2',
      name: 'Quezon City 2',
      version: location.version,
    });
    req.flush(location);
    await promise;
  });

  it('deactivate is a POST to /deactivate', async () => {
    const promise = service.deactivate('l1');
    const req = httpMock.expectOne('/api/locations/l1/deactivate');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ ...location, isActive: false });
    expect((await promise).isActive).toBe(false);
  });
});
