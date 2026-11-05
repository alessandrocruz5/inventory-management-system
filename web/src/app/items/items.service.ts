import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Paged } from '../core/api/paged';

export interface Item {
  id: string;
  sku: string;
  name: string;
  baseUnit: string;
  isActive: boolean;
  version: string;
}

export interface ItemInput {
  sku: string;
  name: string;
  baseUnit: string;
}

export interface ItemQuery {
  search?: string;
  active?: boolean;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class ItemsService {
  private readonly http = inject(HttpClient);

  list(query: ItemQuery): Promise<Paged<Item>> {
    let params = new HttpParams();
    const search = query.search?.trim();
    if (search) {
      params = params.set('search', search);
    }
    if (query.active !== undefined) {
      params = params.set('active', query.active);
    }
    params = params.set('page', query.page).set('pageSize', query.pageSize);
    return firstValueFrom(this.http.get<Paged<Item>>('/api/items', { params }));
  }

  get(id: string): Promise<Item> {
    return firstValueFrom(this.http.get<Item>(`/api/items/${encodeURIComponent(id)}`));
  }

  create(input: ItemInput): Promise<Item> {
    return firstValueFrom(this.http.post<Item>('/api/items', input));
  }

  update(item: Item, input: ItemInput): Promise<Item> {
    return firstValueFrom(
      this.http.put<Item>(`/api/items/${encodeURIComponent(item.id)}`, {
        ...input,
        version: item.version,
      }),
    );
  }

  deactivate(id: string): Promise<Item> {
    return firstValueFrom(
      this.http.post<Item>(`/api/items/${encodeURIComponent(id)}/deactivate`, null),
    );
  }
}
