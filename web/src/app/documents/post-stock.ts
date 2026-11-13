import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormArray,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  Subject,
  debounceTime,
  distinctUntilChanged,
  filter,
  firstValueFrom,
  takeUntil,
} from 'rxjs';
import { Item, ItemsService } from '../items/items.service';
import { Location, LocationsService } from '../locations/locations.service';
import { ConfirmDialog, ConfirmDialogData } from '../shared/confirm-dialog';
import {
  ADJUSTMENT_REASONS,
  AdjustmentReason,
  formatQuantity,
  reasonLabel,
} from '../shared/format';
import { StockService } from '../stock/stock.service';
import { DocumentsService, POSTING_TYPES, PostedDocument, PostingType } from './documents.service';
import {
  IdempotencyKey,
  LineDirection,
  MAX_LINES,
  POSTING_TITLES,
  PostingHeader,
  PostingLine,
  amountValidator,
  buildPostingBody,
  duplicateItemLines,
  linesOverOnHand,
  postingErrors,
  sourceLocationId,
  unitCostRequired,
  usesUnitCost,
} from './posting-form';

type ItemValue = string | Item;

function itemChosen(control: AbstractControl): ValidationErrors | null {
  return typeof control.value === 'object' && control.value !== null ? null : { required: true };
}

@Component({
  selector: 'app-post-stock',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatAutocompleteModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './post-stock.html',
  styleUrl: './post-stock.scss',
})
export class PostStock {
  private readonly documents = inject(DocumentsService);
  private readonly stock = inject(StockService);
  private readonly itemsService = inject(ItemsService);
  private readonly locationsService = inject(LocationsService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly maxLines = MAX_LINES;
  protected readonly reasons = ADJUSTMENT_REASONS;
  protected readonly reasonLabel = reasonLabel;

  protected readonly type = signal<PostingType>('receipt');
  protected readonly title = computed(() => POSTING_TITLES[this.type()]);
  protected readonly locations = signal<Location[]>([]);
  protected readonly options = signal<Item[][]>([[]]);
  protected readonly onHand = signal<(number | null)[]>([null]);
  protected readonly posting = signal(false);
  protected readonly message = signal<string | null>(null);
  protected readonly originalDocumentId = signal<string | null>(null);

  private readonly tick = signal(0);
  private readonly removed = new Subject<FormGroup>();
  private key = new IdempotencyKey();

  protected readonly form = new FormGroup({
    header: new FormGroup({
      locationId: new FormControl<string | null>(null),
      fromLocationId: new FormControl<string | null>(null),
      toLocationId: new FormControl<string | null>(null),
      reasonCode: new FormControl<AdjustmentReason | null>(null),
      reference: new FormControl('', {
        nonNullable: true,
        validators: [Validators.maxLength(100)],
      }),
      notes: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] }),
    }),
    lines: new FormArray<ReturnType<PostStock['makeLine']>>([]),
  });

  protected readonly lineCount = computed(() => {
    this.tick();
    return this.form.controls.lines.length;
  });

  protected readonly lineValues = computed<PostingLine[]>(() => {
    this.tick();
    return this.form.controls.lines.controls.map((g) => this.lineValue(g));
  });

  protected readonly duplicates = computed(() => duplicateItemLines(this.lineValues()));

  protected readonly canSubmit = computed(() => {
    this.tick();
    return !this.posting() && this.form.valid && this.duplicates().length === 0;
  });

  protected readonly itemLabel = (value: ItemValue | null): string =>
    typeof value === 'string' ? value : value ? `${value.sku} · ${value.name}` : '';

  constructor() {
    this.form.events.pipe(takeUntilDestroyed()).subscribe(() => this.tick.update((n) => n + 1));
    const header = this.form.controls.header.controls;
    header.locationId.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.refreshAll());
    header.fromLocationId.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.refreshAll());

    inject(ActivatedRoute)
      .paramMap.pipe(takeUntilDestroyed())
      .subscribe((params) => {
        const type = params.get('type');
        if (POSTING_TYPES.includes(type as PostingType)) {
          this.reset(type as PostingType);
        }
      });

    void this.loadLocations();
  }

  protected showUnitCost(line: ReturnType<PostStock['makeLine']>): boolean {
    this.tick();
    return usesUnitCost(this.type(), this.lineValue(line));
  }

  protected onHandText(i: number): string | null {
    const have = this.onHand()[i];
    if (have == null) {
      return null;
    }
    const unit = this.lineValues()[i]?.itemId ? this.unitOf(this.form.controls.lines.at(i)) : '';
    return `On hand: ${formatQuantity(have)}${unit ? ` ${unit}` : ''}`;
  }

  protected errorText(control: AbstractControl, required: string): string {
    const errors = control.errors;
    if (!errors) {
      return '';
    }
    if (typeof errors['server'] === 'string') {
      return errors['server'];
    }
    if (typeof errors['amount'] === 'string') {
      return errors['amount'];
    }
    if (errors['required']) {
      return required;
    }
    if (errors['maxlength']) {
      return 'Too long.';
    }
    return 'Invalid.';
  }

  protected addLine(): void {
    const lines = this.form.controls.lines;
    if (lines.length >= MAX_LINES) {
      return;
    }
    lines.push(this.makeLine());
    this.options.update((o) => [...o, []]);
    this.onHand.update((o) => [...o, null]);
  }

  protected removeLine(i: number): void {
    const lines = this.form.controls.lines;
    if (lines.length <= 1) {
      return;
    }
    this.removed.next(lines.at(i));
    lines.removeAt(i);
    this.options.update((o) => o.filter((_, j) => j !== i));
    this.onHand.update((o) => o.filter((_, j) => j !== i));
  }

  protected async submit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.posting() || this.form.invalid || this.duplicates().length > 0) {
      return;
    }
    const type = this.type();
    const lines = this.lineValues();
    const over = linesOverOnHand(type, lines, this.onHand());
    if (over.length > 0) {
      const message = over
        .map(
          (i) =>
            `Line ${i + 1} asks for ${formatQuantity(lines[i].quantity!)} but only ${formatQuantity(this.onHand()[i]!)} is on hand.`,
        )
        .join(' ');
      const ok = await firstValueFrom(
        this.dialog
          .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
            data: {
              title: 'Not enough stock?',
              message: `${message} Post anyway?`,
              confirm: 'Post anyway',
            },
          })
          .afterClosed(),
      );
      if (!ok) {
        return;
      }
    }

    this.posting.set(true);
    this.message.set(null);
    this.originalDocumentId.set(null);
    let posted: PostedDocument;
    try {
      const header = this.form.controls.header.getRawValue() as PostingHeader;
      posted = await this.documents.post(
        type,
        this.key.current,
        buildPostingBody(type, header, lines),
      );
    } catch (err) {
      this.showErrors(err, type, lines);
      return;
    } finally {
      this.posting.set(false);
    }
    // The posting is committed: a failed navigation must never read as a failed post.
    this.snackBar.open('Posted', 'OK', { duration: 4000 });
    await this.router.navigate(['/documents', posted.id]).catch(() => false);
  }

  private showErrors(err: unknown, type: PostingType, lines: PostingLine[]): void {
    const units = this.form.controls.lines.controls.map((g) => this.unitOf(g) || null);
    const e = postingErrors(
      type,
      err instanceof HttpErrorResponse ? err.error : null,
      lines,
      units,
    );
    const mark = (control: AbstractControl | null | undefined, text: string | undefined): void => {
      if (control && text !== undefined) {
        control.setErrors({ server: text });
        control.markAsTouched();
      }
    };
    const headerControls = this.form.controls.header.controls as Record<string, AbstractControl>;
    for (const [key, text] of Object.entries(e.header)) {
      mark(Object.hasOwn(headerControls, key) ? headerControls[key] : null, text);
    }
    for (const [index, fields] of Object.entries(e.lines)) {
      const group = this.form.controls.lines.at(Number(index)) as
        ReturnType<PostStock['makeLine']> | undefined;
      if (!group) {
        continue;
      }
      mark(group.controls.item, fields.itemId);
      mark(group.controls.quantity, fields.quantity);
      mark(group.controls.unitCost, fields.unitCost);
    }
    this.message.set(e.message ?? e.header.lines ?? null);
    this.originalDocumentId.set(e.originalDocumentId);
    if (e.renewKey) {
      this.key.renew();
    }
  }

  private reset(type: PostingType): void {
    const lines = this.form.controls.lines;
    lines.controls.forEach((g) => this.removed.next(g));
    lines.clear();
    this.type.set(type);
    this.key = new IdempotencyKey();
    this.message.set(null);
    this.originalDocumentId.set(null);
    this.form.controls.header.reset();
    const h = this.form.controls.header.controls;
    const set = (control: AbstractControl, required: boolean): void => {
      if (required) {
        control.addValidators(Validators.required);
      } else {
        control.removeValidators(Validators.required);
      }
      control.updateValueAndValidity({ emitEvent: false });
    };
    set(h.locationId, type !== 'transfer');
    set(h.fromLocationId, type === 'transfer');
    set(h.toLocationId, type === 'transfer');
    set(h.reasonCode, type === 'adjustment');
    lines.push(this.makeLine());
    this.options.set([[]]);
    this.onHand.set([null]);
  }

  private makeLine() {
    const type = this.type();
    const group = new FormGroup({
      item: new FormControl<ItemValue>('', { nonNullable: true, validators: [itemChosen] }),
      quantity: new FormControl<number | null>(null, [
        Validators.required,
        amountValidator({ allowZero: false }),
      ]),
      unitCost: new FormControl<number | null>(null, [
        ...(unitCostRequired(type) ? [Validators.required] : []),
        amountValidator({ allowZero: true }),
      ]),
      direction: new FormControl<LineDirection>('add', { nonNullable: true }),
    });
    const gone = this.removed.pipe(filter((g) => g === group));

    group.controls.item.valueChanges.pipe(takeUntil(gone)).subscribe((value) => {
      if (typeof value !== 'string') {
        this.refreshOnHand(group);
      } else {
        this.setOnHand(group, null);
      }
    });
    group.controls.item.valueChanges
      .pipe(
        filter((v): v is string => typeof v === 'string'),
        debounceTime(300),
        distinctUntilChanged(),
        takeUntil(gone),
      )
      .subscribe((search) => void this.search(group, search));
    group.controls.direction.valueChanges.pipe(takeUntil(gone)).subscribe((d) => {
      if (d === 'remove') {
        group.controls.unitCost.setValue(null);
      }
      this.refreshOnHand(group);
    });
    return group;
  }

  private lineValue(group: ReturnType<PostStock['makeLine']>): PostingLine {
    const item = group.controls.item.value;
    return {
      itemId: typeof item === 'object' ? item.id : null,
      quantity: group.controls.quantity.value,
      unitCost: group.controls.unitCost.value,
      direction: group.controls.direction.value,
    };
  }

  private unitOf(group: ReturnType<PostStock['makeLine']>): string {
    const item = group.controls.item.value;
    return typeof item === 'object' ? item.baseUnit : '';
  }

  private async search(group: ReturnType<PostStock['makeLine']>, search: string): Promise<void> {
    try {
      const page = await this.itemsService.list({ search, active: true, page: 1, pageSize: 10 });
      const i = this.form.controls.lines.controls.indexOf(group);
      if (i >= 0) {
        this.options.update((o) => o.map((x, j) => (j === i ? page.items : x)));
      }
    } catch {
      // The field just shows no options.
    }
  }

  private setOnHand(group: ReturnType<PostStock['makeLine']>, value: number | null): void {
    const i = this.form.controls.lines.controls.indexOf(group);
    if (i >= 0) {
      this.onHand.update((o) => o.map((x, j) => (j === i ? value : x)));
    }
  }

  private refreshAll(): void {
    this.form.controls.lines.controls.forEach((g) => this.refreshOnHand(g));
  }

  private refreshOnHand(group: ReturnType<PostStock['makeLine']>): void {
    const itemId = this.lineValue(group).itemId;
    const location = sourceLocationId(
      this.type(),
      this.form.controls.header.getRawValue() as PostingHeader,
    );
    this.setOnHand(group, null);
    if (!itemId || !location) {
      return;
    }
    this.stock.onHand(itemId, location).then(
      (n) => {
        const still = sourceLocationId(
          this.type(),
          this.form.controls.header.getRawValue() as PostingHeader,
        );
        if (this.lineValue(group).itemId === itemId && still === location) {
          this.setOnHand(group, n);
        }
      },
      () => undefined,
    );
  }

  private async loadLocations(): Promise<void> {
    try {
      const page = await this.locationsService.list({ page: 1, pageSize: 100 });
      this.locations.set(page.items.filter((l) => !l.isSystem && l.isActive));
    } catch {
      this.locations.set([]);
    }
  }
}
