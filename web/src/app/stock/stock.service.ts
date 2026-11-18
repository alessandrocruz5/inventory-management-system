import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Paged } from '../core/api/paged';

export interface StockRow {
  itemId: string;
  sku: string;
  itemName: string;
  baseUnit: string;
  locationId: string;
  locationCode: string;
  locationName: string;
  onHand: number;
  stockValue: number;
  averageCost: number | null;
  lastUnitCost: number | null;
}

export interface StockTotals {
  stockValue: number;
}

export interface StockPage extends Paged<StockRow> {
  totals: StockTotals;
}

export interface StockQuery {
  itemId?: string;
  locationId?: string;
  page: number;
  pageSize: number;
}

export interface StockBalance {
  onHand: number;
  averageCost: number | null;
}

@Injectable({ providedIn: 'root' })
export class StockService {
  private readonly http = inject(HttpClient);

  list(query: StockQuery): Promise<StockPage> {
    let params = new HttpParams();
    if (query.itemId) {
      params = params.set('itemId', query.itemId);
    }
    if (query.locationId) {
      params = params.set('locationId', query.locationId);
    }
    params = params.set('page', query.page).set('pageSize', query.pageSize);
    return firstValueFrom(this.http.get<StockPage>('/api/stock', { params }));
  }

  async balance(itemId: string, locationId: string): Promise<StockBalance> {
    const page = await this.list({ itemId, locationId, page: 1, pageSize: 1 });
    const row = page.items[0];
    return { onHand: row?.onHand ?? 0, averageCost: row?.averageCost ?? null };
  }

  async onHand(itemId: string, locationId: string): Promise<number> {
    return (await this.balance(itemId, locationId)).onHand;
  }
}
