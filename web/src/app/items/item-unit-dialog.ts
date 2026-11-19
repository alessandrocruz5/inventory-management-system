import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { problemFieldErrors, problemMessage } from '../core/problems/problem-messages';
import { amountValidator } from '../documents/posting-form';
import { ItemUnit, ItemsService } from './items.service';

export interface ItemUnitDialogData {
  itemId: string;
  baseUnit: string;
}

@Component({
  selector: 'app-item-unit-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
  ],
  templateUrl: './item-unit-dialog.html',
  styles: `
    .full-width {
      width: 100%;
    }
    .form-error {
      color: var(--mat-sys-error);
    }
  `,
})
export class ItemUnitDialog {
  private readonly items = inject(ItemsService);
  private readonly dialogRef =
    inject<MatDialogRef<ItemUnitDialog, ItemUnit | undefined>>(MatDialogRef);
  protected readonly data = inject<ItemUnitDialogData>(MAT_DIALOG_DATA);

  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = new FormGroup({
    code: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(16)],
    }),
    factor: new FormControl<number | null>(null, {
      validators: [Validators.required, amountValidator({ allowZero: false })],
    }),
  });

  protected async save(): Promise<void> {
    if (this.form.invalid || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    try {
      const { code, factor } = this.form.getRawValue();
      const unit = await this.items.addUnit(this.data.itemId, {
        code: code.trim(),
        factor: factor as number,
      });
      this.dialogRef.close(unit);
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
