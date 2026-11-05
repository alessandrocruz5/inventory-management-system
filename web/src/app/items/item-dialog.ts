import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { problemFieldErrors, problemMessage } from '../core/problems/problem-messages';
import { Item, ItemsService } from './items.service';

export interface ItemDialogData {
  item: Item | null;
}

@Component({
  selector: 'app-item-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './item-dialog.html',
  styles: `
    .full-width {
      width: 100%;
    }
    .form-error {
      color: var(--mat-sys-error);
    }
  `,
})
export class ItemDialog {
  private readonly items = inject(ItemsService);
  private readonly dialogRef = inject<MatDialogRef<ItemDialog, Item | undefined>>(MatDialogRef);
  protected readonly data = inject<ItemDialogData>(MAT_DIALOG_DATA);

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    sku: new FormControl(this.data.item?.sku ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(64)],
    }),
    name: new FormControl(this.data.item?.name ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(200)],
    }),
    baseUnit: new FormControl(this.data.item?.baseUnit ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(16)],
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
      const saved = this.data.item
        ? await this.items.update(this.data.item, value)
        : await this.items.create(value);
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
