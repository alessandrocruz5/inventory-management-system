import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { amountValidator } from '../documents/posting-form';
import { ItemDetail, ItemUnit, ItemsService } from '../items/items.service';
import { problemFieldErrors, problemMessage, problemSlug } from '../core/problems/problem-messages';
import { formatQuantity } from '../shared/format';
import { StocktakeDetail, StocktakeLine, StocktakesService } from './stocktakes.service';

export interface CountLineDialogData {
  stocktakeId: string;
  locationId: string;
  item: { id: string; sku: string; name: string; baseUnit: string };
  /** The item's line on the sheet; null for an item being added. */
  line: StocktakeLine | null;
}

/** The new detail on a save; 'reload' when the sheet changed under the counter (no longer open). */
export type CountLineDialogResult = StocktakeDetail | 'reload' | undefined;

type Field = 'quantity' | 'unit' | 'unitCost';

@Component({
  selector: 'app-count-line-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  template: `
    <h2 mat-dialog-title>Count {{ data.item.name }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content>
        <p class="muted item-sku">
          <span class="mono">{{ data.item.sku }}</span>
          @if (expected !== null) {
            · Expected {{ formatQuantity(expected) }} {{ data.item.baseUnit }}
          }
        </p>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Counted quantity ({{ entryUnit() }})</mat-label>
          <input
            matInput
            type="number"
            inputmode="decimal"
            class="num"
            formControlName="quantity"
            data-testid="count-quantity"
          />
          <mat-error>{{ errorText('quantity', 'Enter a quantity. Use 0 for none.') }}</mat-error>
        </mat-form-field>
        @if (choices().length > 0) {
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Unit</mat-label>
            <mat-select formControlName="unit" data-testid="count-unit">
              <mat-option value="">{{ data.item.baseUnit }}</mat-option>
              @for (u of choices(); track u.code) {
                <mat-option [value]="u.code"
                  >{{ u.code }} ({{ formatQuantity(u.factor) }}
                  {{ data.item.baseUnit }})</mat-option
                >
              }
            </mat-select>
            <mat-error>{{ errorText('unit', 'Choose a unit.') }}</mat-error>
          </mat-form-field>
        }
        @if (needsCost()) {
          <mat-form-field appearance="outline" class="full-width">
            <mat-label>Unit cost per {{ data.item.baseUnit }} (₱)</mat-label>
            <input
              matInput
              type="number"
              inputmode="decimal"
              class="num"
              formControlName="unitCost"
              data-testid="count-unit-cost"
            />
            <mat-hint>There's no stock to average, so say what a unit costs.</mat-hint>
            <mat-error>{{ errorText('unitCost', 'Enter a unit cost.') }}</mat-error>
          </mat-form-field>
        }
        @if (error(); as message) {
          <p class="form-error" role="alert">{{ message }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" (click)="close()">Cancel</button>
        <button mat-flat-button type="submit" [disabled]="form.invalid || saving()">
          Save count
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .full-width {
      width: 100%;
    }
    .item-sku {
      margin: 0 0 12px;
      font-size: 13px;
    }
    .form-error {
      color: var(--mat-sys-error);
    }
  `,
})
export class CountLineDialog {
  protected readonly data = inject<CountLineDialogData>(MAT_DIALOG_DATA);
  private readonly dialogRef =
    inject<MatDialogRef<CountLineDialog, CountLineDialogResult>>(MatDialogRef);
  private readonly stocktakes = inject(StocktakesService);
  private readonly itemsService = inject(ItemsService);

  protected readonly formatQuantity = formatQuantity;
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly detail = signal<ItemDetail | null>(null);

  protected readonly form = new FormGroup({
    quantity: new FormControl<number | null>(this.data.line?.countedQuantity ?? null, [
      Validators.required,
      amountValidator({ allowZero: true }),
    ]),
    // The chosen unit's code; '' is the item's base unit.
    unit: new FormControl('', { nonNullable: true }),
    unitCost: new FormControl<number | null>(null, [amountValidator({ allowZero: true })]),
  });

  private readonly values = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  protected readonly expected = this.data.line?.expectedQuantity ?? null;

  protected readonly choices = computed<ItemUnit[]>(() =>
    (this.detail()?.units ?? []).filter((u) => u.isActive),
  );

  private readonly factor = computed(() => {
    const code = this.values().unit;
    return (code ? this.choices().find((u) => u.code === code)?.factor : undefined) ?? 1;
  });

  protected readonly entryUnit = computed(() => this.values().unit || this.data.item.baseUnit);

  /** What the system holds now: this line's balance, else the item's balance at the location. */
  private readonly onHand = computed(
    () =>
      this.data.line?.currentOnHand ??
      this.detail()?.balances.find((b) => b.locationId === this.data.locationId)?.onHand ??
      0,
  );

  /** A cost is only needed to count above the expected quantity when there is no stock to average. */
  protected readonly needsCost = computed(() => {
    const quantity = this.values().quantity;
    if (quantity == null || this.onHand() > 0) {
      return false;
    }
    const expected = this.data.line?.expectedQuantity ?? this.onHand();
    return quantity * this.factor() > expected;
  });

  constructor() {
    void this.loadUnits();
  }

  protected errorText(field: Field, required: string): string {
    const errors = this.form.controls[field].errors;
    if (!errors) {
      return '';
    }
    if (typeof errors['server'] === 'string') {
      return errors['server'];
    }
    if (typeof errors['amount'] === 'string') {
      return errors['amount'];
    }
    return errors['required'] ? required : 'Invalid.';
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving()) {
      return;
    }
    const { quantity, unit, unitCost } = this.form.getRawValue();
    this.saving.set(true);
    this.error.set(null);
    try {
      const updated = await this.stocktakes.count(this.data.stocktakeId, this.data.item.id, {
        quantity: quantity!,
        ...(unit ? { unit } : {}),
        ...(this.needsCost() && unitCost !== null ? { unitCost } : {}),
      });
      this.dialogRef.close(updated);
    } catch (err) {
      this.showError(err instanceof HttpErrorResponse ? err.error : null);
    } finally {
      this.saving.set(false);
    }
  }

  protected close(): void {
    this.dialogRef.close(undefined);
  }

  private showError(body: unknown): void {
    if (problemSlug((body as { type?: unknown } | null)?.type) === 'stocktake-not-open') {
      this.dialogRef.close('reload');
      return;
    }
    let unmapped = false;
    const fields = problemFieldErrors(body);
    for (const [key, messages] of Object.entries(fields)) {
      const field = (['quantity', 'unit', 'unitCost'] as const).find(
        (f) => f.toLowerCase() === key.toLowerCase(),
      );
      if (field && messages[0] !== undefined) {
        this.form.controls[field].setErrors({ server: messages[0] });
        this.form.controls[field].markAsTouched();
      } else {
        unmapped = true;
      }
    }
    if (Object.keys(fields).length === 0 || unmapped) {
      this.error.set(problemMessage(body));
    }
  }

  private async loadUnits(): Promise<void> {
    try {
      this.detail.set(await this.itemsService.get(this.data.item.id));
    } catch {
      // The count stays in the base unit.
    }
  }
}
