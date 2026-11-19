import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { problemMessage } from '../core/problems/problem-messages';
import { Location, LocationsService } from '../locations/locations.service';
import { StatusTone, formatManilaDateTime } from '../shared/format';
import { StatusPill } from '../shared/status-pill';
import { StartStocktakeDialog } from './start-stocktake-dialog';
import { StocktakeStatus, StocktakeSummary, StocktakesService } from './stocktakes.service';

const STATUSES: readonly StocktakeStatus[] = ['Draft', 'Posting', 'Posted', 'Cancelled'];

const STATUS_LABELS: Record<StocktakeStatus, string> = {
  Draft: 'Counting',
  Posting: 'Posting…',
  Posted: 'Posted',
  Cancelled: 'Cancelled',
};

const STATUS_TONES: Record<StocktakeStatus, StatusTone> = {
  Draft: 'warn',
  Posting: 'warn',
  Posted: 'good',
  Cancelled: 'neutral',
};

@Component({
  selector: 'app-stocktakes',
  imports: [
    RouterLink,
    MatButtonModule,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatSelectModule,
    StatusPill,
  ],
  templateUrl: './stocktakes.html',
  host: { class: 'page' },
  styleUrl: './stocktakes.scss',
})
export class Stocktakes {
  private readonly stocktakesService = inject(StocktakesService);
  private readonly locationsService = inject(LocationsService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);

  protected readonly pageSizeOptions = [25, 50, 100];
  protected readonly statuses = STATUSES;
  protected readonly statusLabel = (s: StocktakeStatus) => STATUS_LABELS[s];
  protected readonly statusTone = (s: StocktakeStatus) => STATUS_TONES[s];
  protected readonly formatDate = formatManilaDateTime;

  protected readonly rows = signal<StocktakeSummary[]>([]);
  protected readonly locations = signal<Location[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(25);
  protected readonly status = signal<StocktakeStatus | null>(null);
  protected readonly locationId = signal<string | null>(null);

  constructor() {
    void this.loadLocations();
    void this.load();
  }

  protected onStatus(status: StocktakeStatus | null): void {
    this.status.set(status);
    this.pageIndex.set(0);
    void this.load();
  }

  protected onLocation(locationId: string | null): void {
    this.locationId.set(locationId);
    this.pageIndex.set(0);
    void this.load();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    void this.load();
  }

  protected openStart(): void {
    this.dialog
      .open<StartStocktakeDialog, undefined, string | undefined>(StartStocktakeDialog)
      .afterClosed()
      .subscribe((id) => {
        if (id) {
          void this.router.navigate(['/stocktakes', id]);
        }
      });
  }

  private async loadLocations(): Promise<void> {
    try {
      const page = await this.locationsService.list({ page: 1, pageSize: 100 });
      this.locations.set(page.items.filter((l) => l.kind === 'Physical' && l.isActive));
    } catch {
      this.locations.set([]);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const status = this.status();
      const locationId = this.locationId();
      const result = await this.stocktakesService.list({
        ...(status ? { status } : {}),
        ...(locationId ? { locationId } : {}),
        page: this.pageIndex() + 1,
        pageSize: this.pageSize(),
      });
      this.rows.set(result.items);
      this.total.set(result.total);
    } catch (err) {
      this.snackBar.open(
        problemMessage(err instanceof HttpErrorResponse ? err.error : null),
        'OK',
        { duration: 6000 },
      );
    } finally {
      this.loading.set(false);
    }
  }
}
