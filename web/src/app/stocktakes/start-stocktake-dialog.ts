import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { problemMessage, problemSlug } from '../core/problems/problem-messages';
import { Location, LocationsService } from '../locations/locations.service';
import { StocktakesService } from './stocktakes.service';

/** Closes with the id of the stocktake to open: the new one, or the one already in progress. */
@Component({
  selector: 'app-start-stocktake-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  template: `
    <h2 mat-dialog-title>Start a count</h2>
    <form [formGroup]="form" (ngSubmit)="start()">
      <mat-dialog-content>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Location</mat-label>
          <mat-select formControlName="locationId">
            @for (l of locations(); track l.id) {
              <mat-option [value]="l.id">{{ l.code }} · {{ l.name }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Notes (optional)</mat-label>
          <input matInput formControlName="notes" maxlength="500" />
        </mat-form-field>
        @if (error(); as message) {
          <p class="form-error" role="alert">{{ message }}</p>
          @if (existingId(); as id) {
            <button mat-stroked-button type="button" (click)="openExisting(id)">Open it</button>
          }
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" (click)="cancel()">Cancel</button>
        <button mat-flat-button type="submit" [disabled]="form.invalid || saving()">
          Start counting
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .full-width {
      width: 100%;
    }
    .form-error {
      color: var(--mat-sys-error);
    }
  `,
})
export class StartStocktakeDialog {
  private readonly stocktakes = inject(StocktakesService);
  private readonly locationsService = inject(LocationsService);
  private readonly dialogRef =
    inject<MatDialogRef<StartStocktakeDialog, string | undefined>>(MatDialogRef);

  protected readonly locations = signal<Location[]>([]);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly existingId = signal<string | null>(null);

  protected readonly form = new FormGroup({
    locationId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    notes: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
  });

  constructor() {
    void this.loadLocations();
  }

  protected async start(): Promise<void> {
    if (this.form.invalid || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.existingId.set(null);
    try {
      const { locationId, notes } = this.form.getRawValue();
      const created = await this.stocktakes.start({
        locationId,
        notes: notes.trim() || null,
      });
      this.dialogRef.close(created.id);
    } catch (err) {
      const body = err instanceof HttpErrorResponse ? err.error : null;
      this.error.set(problemMessage(body));
      const id = (body as { stocktakeId?: unknown } | null)?.stocktakeId;
      if (problemSlug((body as { type?: unknown } | null)?.type) === 'stocktake-open-exists') {
        this.existingId.set(typeof id === 'string' ? id : null);
      }
    } finally {
      this.saving.set(false);
    }
  }

  protected openExisting(id: string): void {
    this.dialogRef.close(id);
  }

  protected cancel(): void {
    this.dialogRef.close(undefined);
  }

  private async loadLocations(): Promise<void> {
    try {
      const page = await this.locationsService.list({ page: 1, pageSize: 100 });
      this.locations.set(page.items.filter((l) => l.kind === 'Physical' && l.isActive));
    } catch {
      this.locations.set([]);
    }
  }
}
