import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatListModule } from '@angular/material/list';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../core/auth/auth.service';
import { problemMessage } from '../core/problems/problem-messages';
import { ConfirmDialog } from '../shared/confirm-dialog';
import { LocationDialog } from './location-dialog';
import { Location, LocationsService } from './locations.service';

@Component({
  selector: 'app-locations',
  imports: [MatButtonModule, MatChipsModule, MatListModule, MatPaginatorModule],
  templateUrl: './locations.html',
  styleUrl: './locations.scss',
})
export class Locations {
  private readonly locationsService = inject(LocationsService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly auth = inject(AuthService);

  protected readonly pageSizeOptions = [25, 50, 100];

  protected readonly rows = signal<Location[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(25);

  constructor() {
    void this.load();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    void this.load();
  }

  protected openDialog(location: Location | null): void {
    this.dialog
      .open<LocationDialog, { location: Location | null }, Location | undefined>(LocationDialog, {
        data: { location },
      })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) {
          this.snackBar.open('Location saved', 'OK', { duration: 4000 });
          void this.load();
        }
      });
  }

  protected deactivate(location: Location): void {
    this.dialog
      .open<ConfirmDialog, { title: string; message: string; confirm: string }, boolean>(
        ConfirmDialog,
        {
          data: {
            title: 'Deactivate location',
            message: `Deactivate ${location.code} (${location.name})? It can no longer be used for new stock.`,
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
          await this.locationsService.deactivate(location.id);
          this.snackBar.open('Location deactivated', 'OK', { duration: 4000 });
          await this.load();
        } catch (err) {
          this.snackBar.open(
            problemMessage(err instanceof HttpErrorResponse ? err.error : null),
            'OK',
            { duration: 6000 },
          );
        }
      });
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.locationsService.list({
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
