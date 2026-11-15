import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
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
import { ItemDetail as ItemDetailData, ItemMovement, ItemsService } from './items.service';

@Component({
  selector: 'app-item-detail',
  imports: [RouterLink, MatPaginatorModule],
  templateUrl: './item-detail.html',
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 16px;
      padding: 16px;
    }
    h1 {
      margin: 0;
      font: var(--mat-sys-headline-medium);
    }
    h2 {
      margin: 0;
      font: var(--mat-sys-title-medium);
    }
    dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 16px;
      margin: 0;
    }
    dt {
      color: var(--mat-sys-on-surface-variant);
    }
    dd {
      margin: 0;
    }
    table {
      border-collapse: collapse;
      width: 100%;
    }
    th,
    td {
      padding: 8px;
      text-align: left;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .num {
      text-align: right;
    }
    @media (max-width: 599px) {
      thead {
        display: none;
      }
      tr,
      td {
        display: block;
      }
      tr {
        padding: 8px 0;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
      }
      td {
        border: 0;
        padding: 2px 0;
        text-align: left;
      }
      td[data-label]::before {
        content: attr(data-label) ': ';
        color: var(--mat-sys-on-surface-variant);
      }
    }
  `,
})
export class ItemDetail {
  private readonly itemsService = inject(ItemsService);
  private readonly auth = inject(AuthService);
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

  private requestedId: string | null = null;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((params) => {
      const id = params.get('id');
      if (id) {
        void this.load(id);
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
