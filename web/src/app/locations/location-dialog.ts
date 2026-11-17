import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { problemFieldErrors, problemMessage } from '../core/problems/problem-messages';
import { Location, LocationsService } from './locations.service';

export interface LocationDialogData {
  location: Location | null;
}

@Component({
  selector: 'app-location-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './location-dialog.html',
  styles: `
    .full-width {
      width: 100%;
    }
    .form-error {
      color: var(--mat-sys-error);
    }
  `,
})
export class LocationDialog {
  private readonly locations = inject(LocationsService);
  private readonly dialogRef =
    inject<MatDialogRef<LocationDialog, Location | undefined>>(MatDialogRef);
  protected readonly data = inject<LocationDialogData>(MAT_DIALOG_DATA);

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    code: new FormControl(this.data.location?.code ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(32)],
    }),
    name: new FormControl(this.data.location?.name ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(200)],
    }),
  });

  protected async save(): Promise<void> {
    if (this.form.invalid || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    try {
      const value = this.form.getRawValue();
      const saved = this.data.location
        ? await this.locations.update(this.data.location, value)
        : await this.locations.create(value);
      this.dialogRef.close(saved);
    } catch (err) {
      this.showError(err instanceof HttpErrorResponse ? err.error : null);
    } finally {
      this.saving.set(false);
    }
  }

  protected cancel(): void {
    this.dialogRef.close(undefined);
  }

  private showError(body: unknown): void {
    let shown = false;
    for (const [key, messages] of Object.entries(problemFieldErrors(body))) {
      const control = Object.hasOwn(this.form.controls, key) ? this.form.get(key) : null;
      if (control && messages.length > 0) {
        control.setErrors({ server: messages[0] });
        control.markAsTouched();
        shown = true;
      }
    }
    if (!shown) {
      this.error.set(problemMessage(body));
    }
  }
}
