import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { problemMessage } from '../core/problems/problem-messages';
import { ConfirmDialog, ConfirmDialogData } from '../shared/confirm-dialog';
import { formatManilaDateTime, formatMoney, formatQuantity } from '../shared/format';
import {
  BalanceDifference,
  InvariantFailure,
  LedgerHealth as LedgerHealthData,
  LedgerRebuild,
  LedgerService,
} from './ledger.service';

const INVARIANT_LABELS: Readonly<Record<string, string>> = {
  'INV-1': 'Balances match the ledger',
  'INV-2': 'Stock adds up across locations',
  'INV-3': 'No impossible balances',
  'INV-4': 'Reversals mirror their originals',
  'INV-5': 'Last unit cost matches the ledger',
};

const MONEY_CHECKS: ReadonlySet<string> = new Set(['StockValue', 'TotalCost', 'LastUnitCost']);
const QUANTITY_CHECKS: ReadonlySet<string> = new Set(['OnHand', 'OnHandSum', 'Quantity']);

@Component({
  selector: 'app-ledger-health',
  imports: [MatButtonModule],
  templateUrl: './ledger-health.html',
  styleUrl: './ledger-health.scss',
})
export class LedgerHealth {
  private readonly ledger = inject(LedgerService);
  private readonly dialog = inject(MatDialog);

  protected readonly formatDate = formatManilaDateTime;
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;

  protected readonly health = signal<LedgerHealthData | null>(null);
  protected readonly loading = signal(false);
  protected readonly rebuilding = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly rebuild = signal<LedgerRebuild | null>(null);

  constructor() {
    void this.load();
  }

  protected label(id: string, fallback: string): string {
    return INVARIANT_LABELS[id] ?? fallback;
  }

  protected failedCount(h: LedgerHealthData): number {
    return h.invariants.filter((i) => !i.passed).length;
  }

  protected amount(f: InvariantFailure, value: number | null): string {
    if (value === null) {
      return '—';
    }
    if (MONEY_CHECKS.has(f.check)) {
      return formatMoney(value);
    }
    return QUANTITY_CHECKS.has(f.check) ? formatQuantity(value) : String(value);
  }

  protected moneyOrDash(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : formatMoney(value);
  }

  protected movementOrDash(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : String(value);
  }

  protected beforeAfter(d: BalanceDifference, field: 'onHand' | 'stockValue'): string {
    const fmt = field === 'onHand' ? formatQuantity : formatMoney;
    const before = d.before ? fmt(d.before[field]) : '—';
    return `${before} → ${fmt(d.after[field])}`;
  }

  protected async check(): Promise<void> {
    if (this.loading() || this.rebuilding()) {
      return;
    }
    await this.load();
  }

  protected async confirmRebuild(): Promise<void> {
    if (this.loading() || this.rebuilding()) {
      return;
    }
    const confirmed = await new Promise<boolean>((resolve) => {
      this.dialog
        .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
          data: {
            title: 'Rebuild balances',
            message:
              'Recalculate every stock balance from the ledger and fix any that differ? Stock postings wait until it finishes. The ledger itself is never changed.',
            confirm: 'Rebuild',
          },
        })
        .afterClosed()
        .subscribe((result) => resolve(result === true));
    });
    if (!confirmed) {
      return;
    }
    this.rebuilding.set(true);
    this.error.set(null);
    try {
      this.rebuild.set(await this.ledger.rebuild());
      await this.load(true);
    } catch (err) {
      this.error.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
    } finally {
      this.rebuilding.set(false);
    }
  }

  private async load(keepError = false): Promise<void> {
    this.loading.set(true);
    if (!keepError) {
      this.error.set(null);
    }
    try {
      this.health.set(await this.ledger.health());
    } catch (err) {
      this.error.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
    } finally {
      this.loading.set(false);
    }
  }
}
