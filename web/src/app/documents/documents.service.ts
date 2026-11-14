import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Paged } from '../core/api/paged';
import { AdjustmentReason, DocumentType } from '../shared/format';

export type PostingType = 'receipt' | 'issue' | 'transfer' | 'adjustment';

export const POSTING_TYPES: readonly PostingType[] = ['receipt', 'issue', 'transfer', 'adjustment'];

export const POSTING_ROUTES: Record<PostingType, string> = {
  receipt: '/api/documents/receipts',
  issue: '/api/documents/issues',
  transfer: '/api/documents/transfers',
  adjustment: '/api/documents/adjustments',
};

export interface ReceiptBody {
  locationId: string;
  reference: string | null;
  notes: string | null;
  lines: { itemId: string; quantity: number; unitCost: number }[];
}

export interface IssueBody {
  locationId: string;
  reference: string | null;
  notes: string | null;
  lines: { itemId: string; quantity: number }[];
}

export interface TransferBody {
  fromLocationId: string;
  toLocationId: string;
  reference: string | null;
  notes: string | null;
  lines: { itemId: string; quantity: number }[];
}

export interface AdjustmentBody {
  locationId: string;
  reasonCode: AdjustmentReason;
  reference: string | null;
  notes: string | null;
  lines: { itemId: string; quantityDelta: number; unitCost: number | null }[];
}

export type PostingBody = ReceiptBody | IssueBody | TransferBody | AdjustmentBody;

export interface PostedDocument {
  id: string;
  type: DocumentType;
}

export interface DocumentSummary {
  id: string;
  type: DocumentType;
  reasonCode: AdjustmentReason | null;
  reference: string | null;
  postedAt: string;
  postedBy: string;
  lineCount: number;
  reversesDocumentId: string | null;
  reversedByDocumentId: string | null;
}

export interface DocumentLine {
  movementId: number;
  lineNo: number;
  itemId: string;
  sku: string;
  itemName: string;
  baseUnit: string;
  fromLocationId: string;
  fromLocationCode: string;
  fromLocationName: string;
  toLocationId: string;
  toLocationCode: string;
  toLocationName: string;
  quantity: number;
  unitCost: number | null;
  totalCost: number;
  reversesMovementId: number | null;
}

export interface DocumentDetail {
  id: string;
  type: DocumentType;
  reasonCode: AdjustmentReason | null;
  reference: string | null;
  notes: string | null;
  postedAt: string;
  postedBy: string;
  reversesDocumentId: string | null;
  reversedByDocumentId: string | null;
  lines: DocumentLine[];
}

export interface DocumentQuery {
  type?: DocumentType;
  postedFrom?: string;
  postedTo?: string;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class DocumentsService {
  private readonly http = inject(HttpClient);

  list(query: DocumentQuery): Promise<Paged<DocumentSummary>> {
    let params = new HttpParams();
    if (query.type) {
      params = params.set('type', query.type);
    }
    if (query.postedFrom) {
      params = params.set('postedFrom', query.postedFrom);
    }
    if (query.postedTo) {
      params = params.set('postedTo', query.postedTo);
    }
    params = params.set('page', query.page).set('pageSize', query.pageSize);
    return firstValueFrom(this.http.get<Paged<DocumentSummary>>('/api/documents', { params }));
  }

  get(id: string): Promise<DocumentDetail> {
    return firstValueFrom(
      this.http.get<DocumentDetail>(`/api/documents/${encodeURIComponent(id)}`),
    );
  }

  /** A 200 replay and a 201 both resolve here. */
  post(type: PostingType, idempotencyKey: string, body: PostingBody): Promise<PostedDocument> {
    return firstValueFrom(
      this.http.post<PostedDocument>(POSTING_ROUTES[type], body, {
        headers: { 'Idempotency-Key': idempotencyKey },
      }),
    );
  }
}
