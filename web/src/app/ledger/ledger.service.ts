import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type InvariantId = 'INV-1' | 'INV-2' | 'INV-3' | 'INV-4' | 'INV-5';

export interface InvariantFailure {
  itemId: string | null;
  sku: string | null;
  locationId: string | null;
  locationCode: string | null;
  movementId: number | null;
  check: string;
  expected: number | null;
  actual: number | null;
}

export interface InvariantResult {
  id: InvariantId;
  name: string;
  passed: boolean;
  failureCount: number;
  failures: InvariantFailure[];
}

export interface LedgerHealth {
  checkedAt: string;
  healthy: boolean;
  invariants: InvariantResult[];
}

export interface BalanceSnapshot {
  onHand: number;
  stockValue: number;
  lastUnitCost: number | null;
  lastMovementId: number | null;
}

export interface BalanceDifference {
  itemId: string;
  sku: string;
  locationId: string;
  locationCode: string;
  inserted: boolean;
  before: BalanceSnapshot | null;
  after: BalanceSnapshot;
}

export interface LedgerRebuild {
  rebuiltAt: string;
  balancesChecked: number;
  differences: BalanceDifference[];
}

@Injectable({ providedIn: 'root' })
export class LedgerService {
  private readonly http = inject(HttpClient);

  health(): Promise<LedgerHealth> {
    return firstValueFrom(this.http.get<LedgerHealth>('/api/ledger/health'));
  }

  rebuild(): Promise<LedgerRebuild> {
    return firstValueFrom(this.http.post<LedgerRebuild>('/api/ledger/rebuild', null));
  }
}
