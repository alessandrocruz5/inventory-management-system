import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Paged } from '../core/api/paged';

export interface Location {
  id: string;
  code: string;
  name: string;
  kind: 'Physical' | 'Supplier' | 'Customer' | 'Adjustment';
  isSystem: boolean;
  allowNegativeStock: boolean;
  isActive: boolean;
  version: string;
}

export interface LocationInput {
  code: string;
  name: string;
}

export interface LocationQuery {
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class LocationsService {
  private readonly http = inject(HttpClient);

  list(query: LocationQuery): Promise<Paged<Location>> {
    const params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    return firstValueFrom(this.http.get<Paged<Location>>('/api/locations', { params }));
  }

  create(input: LocationInput): Promise<Location> {
    return firstValueFrom(this.http.post<Location>('/api/locations', input));
  }

  update(location: Location, input: LocationInput): Promise<Location> {
    return firstValueFrom(
      this.http.put<Location>(`/api/locations/${encodeURIComponent(location.id)}`, {
        ...input,
        version: location.version,
      }),
    );
  }

  deactivate(id: string): Promise<Location> {
    return firstValueFrom(
      this.http.post<Location>(`/api/locations/${encodeURIComponent(id)}/deactivate`, null),
    );
  }
}
