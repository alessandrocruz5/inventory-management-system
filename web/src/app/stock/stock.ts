import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AllCommunityModule, ColDef, ICellRendererParams, ModuleRegistry } from 'ag-grid-community';
import { AgGridAngular, ICellRendererAngularComp } from 'ag-grid-angular';
import { problemMessage } from '../core/problems/problem-messages';
import { Location, LocationsService } from '../locations/locations.service';
import { formatMoney, formatQuantity, stockStatus } from '../shared/format';
import { appGridTheme } from '../shared/grid-theme';
import { injectIsNarrow } from '../shared/layout';
import { StatusPill } from '../shared/status-pill';
import { StatusPillCell } from '../shared/status-pill-cell';
import { StockRow, StockService } from './stock.service';

ModuleRegistry.registerModules([AllCommunityModule]);

/** Negative balances show in red (spec §10). Applies to the pinned totals row too. */
const NEGATIVE_CELL: ColDef<StockRow>['cellClassRules'] = {
  'stock-negative': (params) => typeof params.value === 'number' && params.value < 0,
};

/** Two-line Item cell: name above SKU. Data is user input, so it is rendered by interpolation only. */
@Component({
  selector: 'app-stock-item-cell',
  template: `
    @if (pinned()) {
      Total
    } @else {
      <span class="name">{{ name() }}</span>
      <span class="sku mono">{{ sku() }}</span>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      justify-content: center;
      height: 100%;
      min-width: 0;
    }
    .name {
      font-size: 14px;
      line-height: 20px;
      font-weight: 500;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .sku {
      font-size: 12px;
      line-height: 16px;
      color: var(--ims-muted);
    }
  `,
})
class StockItemCell implements ICellRendererAngularComp {
  protected readonly pinned = signal(false);
  protected readonly name = signal('');
  protected readonly sku = signal('');

  agInit(params: ICellRendererParams<StockRow>): void {
    this.set(params);
  }

  refresh(params: ICellRendererParams<StockRow>): boolean {
    this.set(params);
    return true;
  }

  private set(params: ICellRendererParams<StockRow>): void {
    this.pinned.set(!!params.node?.rowPinned);
    this.name.set(params.data?.itemName ?? '');
    this.sku.set(params.data?.sku ?? '');
  }
}

@Component({
  selector: 'app-stock',
  imports: [
    AgGridAngular,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatSelectModule,
    RouterLink,
    StatusPill,
  ],
  host: { class: 'page' },
  templateUrl: './stock.html',
  styleUrl: './stock.scss',
})
export class Stock {
  private readonly stockService = inject(StockService);
  private readonly locationsService = inject(LocationsService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);

  protected readonly theme = appGridTheme;
  protected readonly isNarrow = injectIsNarrow();
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;
  protected readonly stockStatus = stockStatus;
  protected readonly skeletonRows = [1, 2, 3, 4, 5];
  protected readonly columnDefs: ColDef<StockRow>[] = [
    {
      colId: 'item',
      headerName: 'Item',
      flex: 2,
      minWidth: 220,
      valueGetter: (params) =>
        params.node?.rowPinned ? 'Total' : `${params.data?.sku} · ${params.data?.itemName}`,
      cellRenderer: StockItemCell,
    },
    {
      field: 'locationCode',
      headerName: 'Location',
      minWidth: 110,
      cellClass: 'mono secondary',
      cellStyle: { fontSize: '13px' },
    },
    {
      colId: 'status',
      headerName: 'Status',
      minWidth: 140,
      sortable: false,
      valueGetter: (params) => (params.data ? stockStatus(params.data.onHand) : undefined),
      cellRenderer: StatusPillCell,
    },
    {
      field: 'onHand',
      headerName: 'Qty',
      type: 'rightAligned',
      cellClass: 'cell-strong',
      cellClassRules: NEGATIVE_CELL,
      valueFormatter: (params) =>
        params.value === undefined || params.value === null ? '' : formatQuantity(params.value),
    },
    { field: 'baseUnit', headerName: 'Unit', minWidth: 80, cellClass: 'muted' },
    {
      field: 'averageCost',
      headerName: 'Avg cost',
      type: 'rightAligned',
      cellClass: 'secondary',
      valueFormatter: (params) =>
        params.value === undefined ? '' : params.value === null ? '—' : formatMoney(params.value),
    },
    {
      field: 'stockValue',
      headerName: 'Value',
      type: 'rightAligned',
      cellClassRules: NEGATIVE_CELL,
      valueFormatter: (params) =>
        params.value === undefined || params.value === null ? '' : formatMoney(params.value),
    },
  ];

  protected readonly pageSizeOptions = [25, 50, 100];

  protected readonly rows = signal<StockRow[]>([]);
  protected readonly total = signal(0);
  protected readonly stockValue = signal(0);
  protected readonly loading = signal(false);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(25);
  protected readonly locationId = signal<string | null>(null);
  protected readonly locations = signal<Location[]>([]);

  protected readonly totalsRow = computed<Partial<StockRow>[]>(() => [
    { stockValue: this.stockValue() },
  ]);

  protected readonly subtitle = computed(() => {
    const n = this.total();
    const m = this.locations().length;
    return `${n} ${n === 1 ? 'balance' : 'balances'} across ${m} ${m === 1 ? 'location' : 'locations'}`;
  });

  constructor() {
    void this.loadLocations();
    void this.load();
  }

  protected onLocation(id: string | null): void {
    this.locationId.set(id);
    this.pageIndex.set(0);
    void this.load();
  }

  protected onRowClicked(row: Partial<StockRow> | undefined): void {
    if (row?.itemId) {
      void this.router.navigate(['/items', row.itemId]);
    }
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    void this.load();
  }

  private async loadLocations(): Promise<void> {
    try {
      const result = await this.locationsService.list({ page: 1, pageSize: 100 });
      this.locations.set(result.items.filter((l) => !l.isSystem));
    } catch (err) {
      this.showError(err);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const locationId = this.locationId();
      const result = await this.stockService.list({
        ...(locationId ? { locationId } : {}),
        page: this.pageIndex() + 1,
        pageSize: this.pageSize(),
      });
      this.rows.set(result.items);
      this.total.set(result.total);
      this.stockValue.set(result.totals.stockValue);
    } catch (err) {
      this.showError(err);
    } finally {
      this.loading.set(false);
    }
  }

  private showError(err: unknown): void {
    this.snackBar.open(problemMessage(err instanceof HttpErrorResponse ? err.error : null), 'OK', {
      duration: 6000,
    });
  }
}
