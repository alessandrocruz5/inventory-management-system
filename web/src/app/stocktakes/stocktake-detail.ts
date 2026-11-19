import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import {
  MatAutocompleteModule,
  MatAutocompleteSelectedEvent,
} from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, filter } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import {
  PROBLEM_MESSAGES,
  problemFieldErrors,
  problemMessage,
  problemSlug,
} from '../core/problems/problem-messages';
import { Item, ItemsService } from '../items/items.service';
import { ConfirmDialog, ConfirmDialogData } from '../shared/confirm-dialog';
import {
  StatusTone,
  formatManilaDateTime,
  formatMoney,
  formatQuantity,
  postedByLabel,
} from '../shared/format';
import { StatusPill } from '../shared/status-pill';
import { CountLineDialog, CountLineDialogData, CountLineDialogResult } from './count-line-dialog';
import {
  StocktakeDetail as StocktakeData,
  StocktakeLine,
  StocktakeStatus,
  StocktakesService,
} from './stocktakes.service';

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

const LINE_ERROR = /^lines\[(\d+)\]\.(quantity|unitCost)$/;

@Component({
  selector: 'app-stocktake-detail',
  imports: [
    NgTemplateOutlet,
    ReactiveFormsModule,
    RouterLink,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    StatusPill,
  ],
  templateUrl: './stocktake-detail.html',
  styleUrl: './stocktake-detail.scss',
  host: { class: 'page' },
})
export class StocktakeDetail {
  private readonly stocktakesService = inject(StocktakesService);
  private readonly itemsService = inject(ItemsService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly statusLabel = (s: StocktakeStatus) => STATUS_LABELS[s];
  protected readonly statusTone = (s: StocktakeStatus) => STATUS_TONES[s];
  protected readonly formatDate = formatManilaDateTime;
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;

  protected readonly stocktake = signal<StocktakeData | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  /** A banner for a failed post or cancel that no row explains. */
  protected readonly banner = signal<string | null>(null);
  /** Per-item messages from a failed post, keyed by item id so they survive a reload. */
  protected readonly rowErrors = signal<Readonly<Record<string, string>>>({});

  protected readonly isAdmin = this.auth.isAdmin;
  protected readonly isDraft = computed(() => this.stocktake()?.status === 'Draft');
  protected readonly isPosting = computed(() => this.stocktake()?.status === 'Posting');
  protected readonly canPost = computed(
    () => this.isAdmin() && (this.isDraft() || this.isPosting()),
  );

  protected readonly countedCount = computed(
    () => (this.stocktake()?.lines ?? []).filter((l) => l.countedQuantity !== null).length,
  );
  protected readonly differenceCount = computed(
    () => (this.stocktake()?.lines ?? []).filter((l) => (l.difference ?? 0) !== 0).length,
  );

  protected readonly search = new FormControl<string | Item>('', { nonNullable: true });
  protected readonly options = signal<Item[]>([]);
  protected readonly itemLabel = (value: string | Item | null): string =>
    typeof value === 'string' ? value : value ? `${value.sku} · ${value.name}` : '';

  private requestedId: string | null = null;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((params) => {
      const id = params.get('id');
      if (id) {
        void this.load(id);
      }
    });
    this.search.valueChanges
      .pipe(
        filter((v): v is string => typeof v === 'string'),
        debounceTime(300),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe((text) => void this.searchItems(text));
  }

  protected startedBy(st: StocktakeData): string {
    return postedByLabel(st.createdBy, this.auth.user()?.id);
  }

  protected differenceText(line: StocktakeLine): string {
    const d = line.difference ?? 0;
    return `${d > 0 ? '+' : ''}${formatQuantity(d)} ${line.baseUnit}`;
  }

  protected estimate(line: StocktakeLine): number | null {
    const d = line.difference;
    if (line.averageCost === null || d === null || d === 0) {
      return null;
    }
    return Math.round(Math.abs(d) * line.averageCost * 10000) / 10000;
  }

  protected openCount(line: StocktakeLine): void {
    if (this.isDraft()) {
      this.openDialog(
        { id: line.itemId, sku: line.sku, name: line.name, baseUnit: line.baseUnit },
        line,
      );
    }
  }

  protected addItem(event: MatAutocompleteSelectedEvent): void {
    const item = event.option.value as Item;
    this.search.setValue('');
    this.options.set([]);
    this.openDialog(item, this.stocktake()?.lines.find((l) => l.itemId === item.id) ?? null);
  }

  protected async post(): Promise<void> {
    const st = this.stocktake();
    if (!st || this.busy() || !this.canPost()) {
      return;
    }
    const n = this.differenceCount();
    const confirmed = await this.confirm({
      title: 'Post count',
      message: `Post ${n} ${n === 1 ? 'correction' : 'corrections'} to ${st.locationName}? This changes stock.`,
      confirm: 'Post count',
    });
    if (!confirmed) {
      return;
    }
    this.busy.set(true);
    this.banner.set(null);
    this.rowErrors.set({});
    try {
      this.stocktake.set(await this.stocktakesService.post(st.id));
    } catch (err) {
      this.showPostError(err instanceof HttpErrorResponse ? err.error : null, st);
      // A rejection puts the stocktake back to Draft; show where it really is.
      await this.refresh(st.id);
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(): Promise<void> {
    const st = this.stocktake();
    if (!st || this.busy() || !this.isAdmin() || !this.isDraft()) {
      return;
    }
    const confirmed = await this.confirm({
      title: 'Cancel count',
      message: `Cancel this count of ${st.locationName}? Nothing is posted, and the counts are kept for reference.`,
      confirm: 'Cancel count',
    });
    if (!confirmed) {
      return;
    }
    this.busy.set(true);
    this.banner.set(null);
    try {
      this.stocktake.set(await this.stocktakesService.cancel(st.id));
    } catch (err) {
      this.banner.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
      await this.refresh(st.id);
    } finally {
      this.busy.set(false);
    }
  }

  private openDialog(item: CountLineDialogData['item'], line: StocktakeLine | null): void {
    const st = this.stocktake();
    if (!st) {
      return;
    }
    this.dialog
      .open<CountLineDialog, CountLineDialogData, CountLineDialogResult>(CountLineDialog, {
        data: { stocktakeId: st.id, locationId: st.locationId, item, line },
      })
      .afterClosed()
      .subscribe((result) => {
        if (result === 'reload') {
          this.snackBar.open(PROBLEM_MESSAGES['stocktake-not-open'], 'OK', { duration: 6000 });
          void this.refresh(st.id);
        } else if (result) {
          this.stocktake.set(result);
        }
      });
  }

  private confirm(data: ConfirmDialogData): Promise<boolean> {
    return new Promise<boolean>((resolve) => {
      this.dialog
        .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
        .afterClosed()
        .subscribe((result) => resolve(result === true));
    });
  }

  private showPostError(body: unknown, st: StocktakeData): void {
    const rows: Record<string, string> = {};
    let banner: string | null = null;
    const slug = problemSlug((body as { type?: unknown } | null)?.type);
    if (slug === 'validation') {
      for (const [key, messages] of Object.entries(problemFieldErrors(body))) {
        const text = messages[0];
        const match = LINE_ERROR.exec(key);
        const line = match ? st.lines[Number(match[1])] : undefined;
        if (line && text !== undefined) {
          rows[line.itemId] ??= text;
        } else {
          banner ??= text ?? problemMessage(body);
        }
      }
      banner ??= Object.keys(rows).length === 0 ? problemMessage(body) : null;
    } else if (slug === 'insufficient-stock') {
      const fields = body as { itemId?: unknown; available?: unknown; requested?: unknown };
      const line = st.lines.find((l) => l.itemId === fields.itemId);
      const num = (v: unknown) => (typeof v === 'number' ? formatQuantity(v) : String(v));
      if (line) {
        rows[line.itemId] =
          `Only ${num(fields.available)} ${line.baseUnit} available; the count would take ${num(fields.requested)} ${line.baseUnit} out.`;
      } else {
        banner = problemMessage(body);
      }
    } else {
      banner = problemMessage(body);
    }
    this.rowErrors.set(rows);
    this.banner.set(banner);
    const first = st.lines.find((l) => rows[l.itemId] !== undefined);
    if (first) {
      setTimeout(
        () =>
          document.getElementById(`line-${first.itemId}`)?.scrollIntoView?.({ block: 'center' }),
        0,
      );
    }
  }

  private async searchItems(text: string): Promise<void> {
    try {
      const page = await this.itemsService.list({
        search: text,
        active: true,
        page: 1,
        pageSize: 10,
      });
      this.options.set(page.items);
    } catch {
      this.options.set([]);
    }
  }

  /** Reload the sheet without clearing the row messages. */
  private async refresh(id: string): Promise<void> {
    try {
      const st = await this.stocktakesService.get(id);
      if (this.requestedId === id) {
        this.stocktake.set(st);
      }
    } catch {
      // Keep what is on screen.
    }
  }

  private async load(id: string): Promise<void> {
    this.requestedId = id;
    this.loading.set(true);
    this.error.set(null);
    this.banner.set(null);
    this.rowErrors.set({});
    this.stocktake.set(null);
    try {
      const st = await this.stocktakesService.get(id);
      if (this.requestedId === id) {
        this.stocktake.set(st);
      }
    } catch (err) {
      if (this.requestedId === id) {
        this.error.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
      }
    } finally {
      if (this.requestedId === id) {
        this.loading.set(false);
      }
    }
  }
}
