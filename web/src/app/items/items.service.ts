import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Paged } from '../core/api/paged';
import { DocumentType } from '../shared/format';

export interface Item {
  id: string;
  sku: string;
  name: string;
  baseUnit: string;
  isActive: boolean;
  version: string;
}

export interface ItemBalance {
  locationId: string;
  locationCode: string;
  locationName: string;
  onHand: number;
  stockValue: number;
  averageCost: number | null;
  lastUnitCost: number | null;
}

export interface ItemDetail extends Item {
  balances: ItemBalance[];
}

export interface ItemMovement {
  movementId: number;
  documentId: string;
  documentType: DocumentType;
  lineNo: number;
  postedAt: string;
  postedBy: string;
  direction: 'In' | 'Out';
  locationId: string;
  locationCode: string;
  locationName: string;
  counterpartLocationId: string;
  counterpartLocationCode: string;
  counterpartLocationName: string;
  quantityChange: number;
  valueChange: number;
  runningOnHand: number;
  runningValue: number;
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

  get(id: string): Promise<ItemDetail> {
    return firstValueFrom(this.http.get<ItemDetail>(`/api/items/${encodeURIComponent(id)}`));
  }

  movements(id: string, page: number, pageSize: number): Promise<Paged<ItemMovement>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return firstValueFrom(
      this.http.get<Paged<ItemMovement>>(`/api/items/${encodeURIComponent(id)}/movements`, {
        params,
      }),
    );
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
