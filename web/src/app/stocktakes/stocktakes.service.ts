import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Paged } from '../core/api/paged';

export type StocktakeStatus = 'Draft' | 'Posting' | 'Posted' | 'Cancelled';

export interface StocktakeSummary {
  id: string;
  locationId: string;
  locationCode: string;
  locationName: string;
  status: StocktakeStatus;
  notes: string | null;
  createdAt: string;
  createdBy: string;
  lineCount: number;
  countedCount: number;
  postedDocumentId: string | null;
}

export interface StocktakeLine {
  itemId: string;
  sku: string;
  name: string;
  baseUnit: string;
  itemIsActive: boolean;
  expectedQuantity: number | null;
  countedQuantity: number | null;
  difference: number | null;
  unitCost: number | null;
  countedAt: string | null;
  countedBy: string | null;
  currentOnHand: number;
  averageCost: number | null;
  movedSinceCount: boolean;
}

export interface StocktakeDetail extends StocktakeSummary {
  updatedAt: string;
  lines: StocktakeLine[];
}

export interface StocktakeQuery {
  status?: StocktakeStatus;
  locationId?: string;
  page: number;
  pageSize: number;
}

export interface StartStocktakeInput {
  locationId: string;
  notes: string | null;
}

export interface CountInput {
  quantity: number;
  unit?: string;
  unitCost?: number | null;
}

@Injectable({ providedIn: 'root' })
export class StocktakesService {
  private readonly http = inject(HttpClient);

  list(query: StocktakeQuery): Promise<Paged<StocktakeSummary>> {
    let params = new HttpParams();
    if (query.status) {
      params = params.set('status', query.status);
    }
    if (query.locationId) {
      params = params.set('locationId', query.locationId);
    }
    params = params.set('page', query.page).set('pageSize', query.pageSize);
    return firstValueFrom(this.http.get<Paged<StocktakeSummary>>('/api/stocktakes', { params }));
  }

  get(id: string): Promise<StocktakeDetail> {
    return firstValueFrom(
      this.http.get<StocktakeDetail>(`/api/stocktakes/${encodeURIComponent(id)}`),
    );
  }

  start(input: StartStocktakeInput): Promise<StocktakeDetail> {
    return firstValueFrom(this.http.post<StocktakeDetail>('/api/stocktakes', input));
  }

  count(id: string, itemId: string, input: CountInput): Promise<StocktakeDetail> {
    return firstValueFrom(
      this.http.put<StocktakeDetail>(
        `/api/stocktakes/${encodeURIComponent(id)}/lines/${encodeURIComponent(itemId)}`,
        input,
      ),
    );
  }

  post(id: string): Promise<StocktakeDetail> {
    return firstValueFrom(
      this.http.post<StocktakeDetail>(`/api/stocktakes/${encodeURIComponent(id)}/post`, {}),
    );
  }

  cancel(id: string): Promise<StocktakeDetail> {
    return firstValueFrom(
      this.http.post<StocktakeDetail>(`/api/stocktakes/${encodeURIComponent(id)}/cancel`, {}),
    );
  }
}
