import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AllCommunityModule, ColDef, ModuleRegistry, themeQuartz } from 'ag-grid-community';
import { AgGridAngular } from 'ag-grid-angular';
import { problemMessage } from '../core/problems/problem-messages';
import { Location, LocationsService } from '../locations/locations.service';
import { formatMoney, formatQuantity } from '../shared/format';
import { StockRow, StockService } from './stock.service';

ModuleRegistry.registerModules([AllCommunityModule]);

@Component({
  selector: 'app-stock',
  imports: [AgGridAngular, MatFormFieldModule, MatPaginatorModule, MatSelectModule],
  templateUrl: './stock.html',
  styleUrl: './stock.scss',
})
export class Stock {
  private readonly stockService = inject(StockService);
  private readonly locationsService = inject(LocationsService);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly theme = themeQuartz;
  protected readonly columnDefs: ColDef<StockRow>[] = [
    {
      headerName: 'Item',
      flex: 2,
      minWidth: 200,
      valueGetter: (params) =>
        params.node?.rowPinned ? 'Total' : `${params.data?.sku} · ${params.data?.itemName}`,
    },
    { field: 'locationCode', headerName: 'Location', minWidth: 110 },
    {
      field: 'onHand',
      headerName: 'Qty',
      type: 'rightAligned',
      valueFormatter: (params) =>
        params.value === undefined || params.value === null ? '' : formatQuantity(params.value),
    },
    { field: 'baseUnit', headerName: 'Unit', minWidth: 80 },
    {
      field: 'averageCost',
      headerName: 'Avg cost',
      type: 'rightAligned',
      valueFormatter: (params) =>
        params.value === undefined ? '' : params.value === null ? '—' : formatMoney(params.value),
    },
    {
      field: 'stockValue',
      headerName: 'Value',
      type: 'rightAligned',
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

  constructor() {
    void this.loadLocations();
    void this.load();
  }

  protected onLocation(id: string | null): void {
    this.locationId.set(id);
    this.pageIndex.set(0);
    void this.load();
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
