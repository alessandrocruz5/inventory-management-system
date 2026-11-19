import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { problemMessage } from '../core/problems/problem-messages';
import {
  documentTypeLabel,
  formatManilaDateTime,
  formatMoney,
  formatQuantity,
  postedByLabel,
} from '../shared/format';
import { ConfirmDialog, ConfirmDialogData } from '../shared/confirm-dialog';
import { StatusPill } from '../shared/status-pill';
import { ItemUnitDialog, ItemUnitDialogData } from './item-unit-dialog';
import {
  ItemDetail as ItemDetailData,
  ItemMovement,
  ItemUnit,
  ItemsService,
} from './items.service';

@Component({
  selector: 'app-item-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatPaginatorModule, StatusPill],
  templateUrl: './item-detail.html',
  styleUrl: './item-detail.scss',
  host: { class: 'page' },
})
export class ItemDetail {
  private readonly itemsService = inject(ItemsService);
  protected readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly route = inject(ActivatedRoute);

  protected readonly typeLabel = documentTypeLabel;
  protected readonly formatDate = formatManilaDateTime;
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;

  protected readonly item = signal<ItemDetailData | null>(null);
  protected readonly movements = signal<ItemMovement[]>([]);
  protected readonly total = signal(0);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(25);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly pageSizeOptions = [25, 50, 100];

  protected readonly totalOnHand = computed(() => {
    const sum = (this.item()?.balances ?? []).reduce((acc, b) => acc + b.onHand, 0);
    return Math.round(sum * 10000) / 10000;
  });

  private requestedId: string | null = null;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((params) => {
      const id = params.get('id');
      if (id) {
        void this.load(id);
      }
    });
  }

  protected addUnit(item: ItemDetailData): void {
    this.dialog
      .open<ItemUnitDialog, ItemUnitDialogData, ItemUnit | undefined>(ItemUnitDialog, {
        data: { itemId: item.id, baseUnit: item.baseUnit },
      })
      .afterClosed()
      .subscribe((unit) => {
        if (unit) {
          this.snackBar.open('Unit added', 'OK', { duration: 4000 });
          void this.reloadItem(item.id);
        }
      });
  }

  protected deactivateUnit(item: ItemDetailData, unit: ItemUnit): void {
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Deactivate unit',
          message: `Deactivate ${unit.code}? It stays on past documents; you can't post in it any more.`,
          confirm: 'Deactivate',
        },
      })
      .afterClosed()
      .subscribe(async (confirmed) => {
        if (!confirmed) {
          return;
        }
        try {
          await this.itemsService.deactivateUnit(item.id, unit.code);
          this.snackBar.open('Unit deactivated', 'OK', { duration: 4000 });
          await this.reloadItem(item.id);
        } catch (err) {
          this.snackBar.open(
            problemMessage(err instanceof HttpErrorResponse ? err.error : null),
            'OK',
            { duration: 6000 },
          );
        }
      });
  }

  protected signedQuantity(q: number): string {
    return q > 0 ? '+' + formatQuantity(q) : formatQuantity(q);
  }

  protected signedMoney(v: number): string {
    return v > 0 ? '+' + formatMoney(v) : formatMoney(v);
  }

  protected counterpart(m: ItemMovement): string {
    return m.direction === 'In'
      ? 'from ' + m.counterpartLocationCode
      : 'to ' + m.counterpartLocationCode;
  }

  protected postedBy(m: ItemMovement): string {
    return postedByLabel(m.postedBy, this.auth.user()?.id);
  }

  protected async onPage(event: PageEvent): Promise<void> {
    const id = this.requestedId;
    if (!id) {
      return;
    }
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.loading.set(true);
    try {
      const page = await this.itemsService.movements(id, event.pageIndex + 1, event.pageSize);
      if (this.requestedId === id) {
        this.movements.set(page.items);
        this.total.set(page.total);
      }
    } catch (err) {
      if (this.requestedId === id) {
        this.error.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
      }
    } finally {
      if (this.requestedId === id) {
        this.loading.set(false);
      }
    }
  }

  private async reloadItem(id: string): Promise<void> {
    try {
      const item = await this.itemsService.get(id);
      if (this.requestedId === id) {
        this.item.set(item);
      }
    } catch (err) {
      if (this.requestedId === id) {
        this.error.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
      }
    }
  }

  private async load(id: string): Promise<void> {
    this.requestedId = id;
    this.loading.set(true);
    this.error.set(null);
    this.item.set(null);
    this.movements.set([]);
    this.total.set(0);
    this.pageIndex.set(0);
    try {
      const [item, page] = await Promise.all([
        this.itemsService.get(id),
        this.itemsService.movements(id, 1, this.pageSize()),
      ]);
      if (this.requestedId === id) {
        this.item.set(item);
        this.movements.set(page.items);
        this.total.set(page.total);
      }
    } catch (err) {
      if (this.requestedId === id) {
        this.error.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
      }
    } finally {
      if (this.requestedId === id) {
        this.loading.set(false);
      }
    }
  }
}
