import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import {
  AllCommunityModule,
  ColDef,
  GridOptions,
  ModuleRegistry,
  SelectionChangedEvent,
  themeQuartz,
} from 'ag-grid-community';
import { AgGridAngular } from 'ag-grid-angular';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { problemMessage } from '../core/problems/problem-messages';
import { ConfirmDialog } from '../shared/confirm-dialog';
import { ItemDialog } from './item-dialog';
import { Item, ItemsService } from './items.service';

ModuleRegistry.registerModules([AllCommunityModule]);

@Component({
  selector: 'app-items',
  imports: [
    ReactiveFormsModule,
    AgGridAngular,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatPaginatorModule,
    MatSlideToggleModule,
  ],
  templateUrl: './items.html',
  styleUrl: './items.scss',
})
export class Items {
  private readonly itemsService = inject(ItemsService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly auth = inject(AuthService);

  protected readonly theme = themeQuartz;
  protected readonly rowSelection: GridOptions<Item>['rowSelection'] = {
    mode: 'singleRow',
    checkboxes: false,
    enableClickSelection: true,
  };
  protected readonly columnDefs: ColDef<Item>[] = [
    { field: 'sku', headerName: 'SKU', flex: 1, minWidth: 140 },
    { field: 'name', headerName: 'Name', flex: 2, minWidth: 200 },
    { field: 'baseUnit', headerName: 'Unit', flex: 1, minWidth: 100 },
    {
      headerName: 'Status',
      flex: 1,
      minWidth: 110,
      valueGetter: (params) => (params.data?.isActive ? 'Active' : 'Inactive'),
    },
  ];

  protected readonly pageSizeOptions = [25, 50, 100];

  protected readonly rows = signal<Item[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly selected = signal<Item | null>(null);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(25);
  protected readonly showInactive = signal(false);
  protected readonly canDeactivate = computed(() => this.selected()?.isActive === true);

  protected readonly search = new FormControl('', { nonNullable: true });

  constructor() {
    this.search.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe(() => {
        this.pageIndex.set(0);
        void this.load();
      });
    void this.load();
  }

  protected onSelectionChanged(event: SelectionChangedEvent<Item>): void {
    this.selected.set(event.api.getSelectedRows()[0] ?? null);
  }

  protected onShowInactive(checked: boolean): void {
    this.showInactive.set(checked);
    this.pageIndex.set(0);
    void this.load();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    void this.load();
  }

  protected openDialog(item: Item | null): void {
    this.dialog
      .open<ItemDialog, { item: Item | null }, Item | undefined>(ItemDialog, { data: { item } })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) {
          this.snackBar.open('Item saved', 'OK', { duration: 4000 });
          void this.load();
        }
      });
  }

  protected deactivate(): void {
    const item = this.selected();
    if (!item) {
      return;
    }
    this.dialog
      .open<ConfirmDialog, { title: string; message: string; confirm: string }, boolean>(
        ConfirmDialog,
        {
          data: {
            title: 'Deactivate item',
            message: `Deactivate ${item.sku} (${item.name})? It can no longer be used for new stock.`,
            confirm: 'Deactivate',
          },
        },
      )
      .afterClosed()
      .subscribe(async (confirmed) => {
        if (!confirmed) {
          return;
        }
        try {
          await this.itemsService.deactivate(item.id);
          this.snackBar.open('Item deactivated', 'OK', { duration: 4000 });
          await this.load();
        } catch (err) {
          this.snackBar.open(
            problemMessage(err instanceof HttpErrorResponse ? err.error : null),
            'OK',
            {
              duration: 6000,
            },
          );
        }
      });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const search = this.search.value.trim();
      const result = await this.itemsService.list({
        ...(search ? { search } : {}),
        ...(this.showInactive() ? {} : { active: true }),
        page: this.pageIndex() + 1,
        pageSize: this.pageSize(),
      });
      this.rows.set(result.items);
      this.total.set(result.total);
      this.selected.set(null);
    } catch (err) {
      this.snackBar.open(
        problemMessage(err instanceof HttpErrorResponse ? err.error : null),
        'OK',
        {
          duration: 6000,
        },
      );
    } finally {
      this.loading.set(false);
    }
  }
}
