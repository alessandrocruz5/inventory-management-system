import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { ItemsService } from '../items/items.service';
import { CountLineDialog } from './count-line-dialog';
import { StocktakeDetail } from './stocktake-detail';
import {
  StocktakeDetail as StocktakeData,
  StocktakeLine,
  StocktakesService,
} from './stocktakes.service';

function line(over: Partial<StocktakeLine> = {}): StocktakeLine {
  return {
    itemId: 'i1',
    sku: 'RICE-WHT',
    name: 'White rice',
    baseUnit: 'kg',
    itemIsActive: true,
    expectedQuantity: 50,
    countedQuantity: 40,
    difference: -10,
    unitCost: null,
    countedAt: '2026-11-19T02:10:00Z',
    countedBy: 'u-1',
    currentOnHand: 50,
    averageCost: 60,
    movedSinceCount: false,
    ...over,
  };
}

function stocktake(over: Partial<StocktakeData> = {}): StocktakeData {
  return {
    id: 's1',
    locationId: 'loc-1',
    locationCode: 'KITCHEN',
    locationName: 'Kitchen',
    status: 'Draft',
    notes: null,
    createdAt: '2026-11-19T02:00:00Z',
    createdBy: 'u-1',
    lineCount: 2,
    countedCount: 2,
    postedDocumentId: null,
    updatedAt: '2026-11-19T02:10:00Z',
    lines: [
      line(),
      line({
        itemId: 'i2',
        sku: 'SUGAR-BRN',
        name: 'Brown sugar',
        expectedQuantity: 20,
        countedQuantity: 20,
        difference: 0,
      }),
    ],
    ...over,
  };
}

describe('StocktakeDetail', () => {
  const get = vi.fn();
  const post = vi.fn();
  const cancel = vi.fn();
  const open = vi.fn();
  const isAdmin = signal(false);
  const params = new BehaviorSubject(convertToParamMap({ id: 's1' }));
  let confirmed = true;

  beforeEach(async () => {
    get.mockReset();
    post.mockReset();
    cancel.mockReset();
    open.mockReset();
    isAdmin.set(false);
    confirmed = true;
    get.mockResolvedValue(stocktake());
    open.mockImplementation(() => ({ afterClosed: () => of(confirmed) }));
    params.next(convertToParamMap({ id: 's1' }));
    await TestBed.configureTestingModule({
      imports: [StocktakeDetail],
      providers: [
        provideRouter([]),
        { provide: StocktakesService, useValue: { get, post, cancel } },
        { provide: ItemsService, useValue: { list: vi.fn() } },
        { provide: AuthService, useValue: { user: signal({ id: 'u-1' }), isAdmin } },
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    }).compileComponents();
    TestBed.overrideProvider(MatDialog, { useValue: { open } });
  });

  async function render() {
    const fixture = TestBed.createComponent(StocktakeDetail);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  const buttons = (el: HTMLElement) => Array.from(el.querySelectorAll('button'));
  const byText = (el: HTMLElement, text: string) =>
    buttons(el).find((b) => b.textContent?.includes(text));

  it('renders the expected, counted and difference values', async () => {
    const el = (await render()).nativeElement as HTMLElement;
    expect(el.querySelector('h1')?.textContent).toContain('Kitchen');
    const lines = Array.from(el.querySelectorAll('[data-testid="line"]'));
    expect(lines).toHaveLength(2);
    expect(lines[0].textContent).toContain('RICE-WHT');
    expect(lines[0].textContent).toContain('Expected 50 kg');
    expect(lines[0].textContent).toContain('Counted 40 kg');
    expect(lines[0].querySelector('[data-testid="difference"]')?.textContent).toContain('-10 kg');
    expect(lines[0].querySelector('[data-testid="difference"]')?.classList).toContain('tone-bad');
    expect(lines[0].textContent).toContain('≈ ₱600.00');
    expect(lines[1].querySelector('[data-testid="difference"]')?.textContent).toContain('0 kg');
    expect(lines[1].textContent).not.toContain('≈');
    expect(el.querySelector('[data-testid="summary"]')?.textContent).toContain(
      '2 of 2 counted · 1 difference',
    );
  });

  it('shows "Not counted" and no estimate for an uncounted line, and flags a moved one', async () => {
    get.mockResolvedValue(
      stocktake({
        lines: [
          line({
            expectedQuantity: null,
            countedQuantity: null,
            difference: null,
            averageCost: null,
            movedSinceCount: false,
          }),
          line({ itemId: 'i2', sku: 'Z', movedSinceCount: true }),
        ],
      }),
    );
    const el = (await render()).nativeElement as HTMLElement;
    const lines = Array.from(el.querySelectorAll('[data-testid="line"]'));
    expect(lines[0].textContent).toContain('Not counted');
    expect(lines[0].textContent).not.toContain('Expected');
    expect(lines[1].querySelector('[data-testid="moved"]')?.textContent).toContain(
      'Stock moved since this count — recount?',
    );
  });

  it('shows Post and Cancel to an Admin only', async () => {
    let el = (await render()).nativeElement as HTMLElement;
    expect(byText(el, 'Post count')).toBeUndefined();
    expect(byText(el, 'Cancel count')).toBeUndefined();
    isAdmin.set(true);
    el = (await render()).nativeElement as HTMLElement;
    expect(byText(el, 'Post count')).toBeDefined();
    expect(byText(el, 'Cancel count')).toBeDefined();
  });

  it('offers Post but not Cancel in the Posting state, with the unfinished notice', async () => {
    isAdmin.set(true);
    get.mockResolvedValue(stocktake({ status: 'Posting' }));
    const el = (await render()).nativeElement as HTMLElement;
    expect(el.textContent).toContain("Posting didn't finish. Post again to complete it.");
    expect(byText(el, 'Post count')).toBeDefined();
    expect(byText(el, 'Cancel count')).toBeUndefined();
  });

  it('posts on confirm with the correction count in the message', async () => {
    isAdmin.set(true);
    post.mockResolvedValue(stocktake({ status: 'Posted', postedDocumentId: 'doc-1' }));
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;
    byText(el, 'Post count')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(open.mock.calls[0][1].data.message).toBe(
      'Post 1 correction to Kitchen? This changes stock.',
    );
    expect(post).toHaveBeenCalledWith('s1');
    expect(el.querySelector('a[href="/documents/doc-1"]')?.textContent).toContain(
      'View the correction',
    );
    expect(byText(el, 'Post count')).toBeUndefined();
  });

  it('does not post when the confirmation is declined', async () => {
    isAdmin.set(true);
    confirmed = false;
    const fixture = await render();
    byText(fixture.nativeElement, 'Post count')!.click();
    await fixture.whenStable();
    expect(post).not.toHaveBeenCalled();
  });

  it('highlights the second row for a remapped lines[1].unitCost error', async () => {
    isAdmin.set(true);
    post.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: {
          type: '/problems/validation',
          errors: { 'lines[1].unitCost': ['Unit cost is required.'] },
        },
      }),
    );
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;
    byText(el, 'Post count')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    const rows = Array.from(el.querySelectorAll('.line'));
    expect(rows[0].classList).not.toContain('line--flag');
    expect(rows[1].classList).toContain('line--flag');
    expect(rows[1].querySelector('[data-testid="line-error"]')?.textContent).toContain(
      'Unit cost is required.',
    );
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('highlights the row of an insufficient-stock item', async () => {
    isAdmin.set(true);
    post.mockRejectedValue(
      new HttpErrorResponse({
        status: 422,
        error: {
          type: '/problems/insufficient-stock',
          itemId: 'i1',
          available: 5,
          requested: 10,
        },
      }),
    );
    const fixture = await render();
    const el = fixture.nativeElement as HTMLElement;
    byText(el, 'Post count')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('.line--flag')?.textContent).toContain('Only 5 kg available');
  });

  it('shows the document link when Posted and the no-differences text when nothing posted', async () => {
    get.mockResolvedValue(stocktake({ status: 'Posted', postedDocumentId: 'doc-9' }));
    let el = (await render()).nativeElement as HTMLElement;
    expect(el.querySelector('a[href="/documents/doc-9"]')?.textContent).toContain(
      'View the correction',
    );
    expect(el.querySelector('input')).toBeNull();
    expect(el.querySelector('button[data-testid="line"]')).toBeNull();

    get.mockResolvedValue(stocktake({ status: 'Posted', postedDocumentId: null }));
    el = (await render()).nativeElement as HTMLElement;
    expect(el.textContent).toContain('No differences — nothing was posted.');
  });

  it('opens the count dialog for a tapped row while Draft', async () => {
    open.mockReturnValue({ afterClosed: () => of(undefined) });
    const fixture = await render();
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('button[data-testid="line"]')!
      .click();
    expect(open).toHaveBeenCalledTimes(1);
    expect(open.mock.calls[0][0]).toBe(CountLineDialog);
    expect(open.mock.calls[0][1].data).toMatchObject({
      stocktakeId: 's1',
      locationId: 'loc-1',
      item: { id: 'i1', sku: 'RICE-WHT' },
    });
  });
});

describe('CountLineDialog', () => {
  const count = vi.fn();
  const itemGet = vi.fn();
  const close = vi.fn();

  const item = { id: 'i1', sku: 'RICE-WHT', name: 'White rice', baseUnit: 'kg' };

  function setup(data: { line: StocktakeLine | null }) {
    TestBed.configureTestingModule({
      imports: [CountLineDialog],
      providers: [
        {
          provide: MAT_DIALOG_DATA,
          useValue: { stocktakeId: 's1', locationId: 'loc-1', item, ...data },
        },
        { provide: MatDialogRef, useValue: { close } },
        { provide: StocktakesService, useValue: { count } },
        { provide: ItemsService, useValue: { get: itemGet } },
      ],
    });
    const fixture = TestBed.createComponent(CountLineDialog);
    const component = fixture.componentInstance as unknown as {
      form: {
        patchValue(v: { quantity?: number; unit?: string; unitCost?: number }): void;
      };
      save(): Promise<void>;
    };
    return { fixture, el: fixture.nativeElement as HTMLElement, component };
  }

  beforeEach(() => {
    count.mockReset();
    close.mockReset();
    itemGet.mockReset();
    itemGet.mockResolvedValue({
      ...item,
      isActive: true,
      version: 'v',
      balances: [],
      units: [
        { code: 'sack', factor: 25, isActive: true },
        { code: 'old', factor: 5, isActive: false },
      ],
    });
  });

  it('sends the unit when it is not the base unit', async () => {
    count.mockResolvedValue({ id: 's1' });
    const { fixture, el, component } = setup({ line: line() });
    await fixture.whenStable();
    fixture.detectChanges();
    el.querySelector<HTMLElement>('[data-testid="count-unit"] .mat-mdc-select-trigger')!.click();
    fixture.detectChanges();
    const panel = document.body.textContent ?? '';
    expect(panel).toContain('sack (25 kg)');
    expect(panel).not.toContain('old (');
    component.form.patchValue({ quantity: 4, unit: 'sack' });
    await component.save();
    expect(count).toHaveBeenCalledWith('s1', 'i1', { quantity: 4, unit: 'sack' });
    expect(close).toHaveBeenCalledWith({ id: 's1' });
  });

  it('omits the unit for the base unit and sends zero', async () => {
    count.mockResolvedValue({ id: 's1' });
    const { fixture, component } = setup({ line: line() });
    await fixture.whenStable();
    component.form.patchValue({ quantity: 0 });
    await component.save();
    expect(count).toHaveBeenCalledWith('s1', 'i1', { quantity: 0 });
  });

  it('asks for a unit cost only when counting above the expected quantity with no stock', async () => {
    count.mockResolvedValue({ id: 's1' });
    const empty = line({
      expectedQuantity: 0,
      currentOnHand: 0,
      countedQuantity: null,
      difference: null,
    });
    const { fixture, el, component } = setup({ line: empty });
    await fixture.whenStable();
    component.form.patchValue({ quantity: 2, unit: 'sack' });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="count-unit-cost"]')).not.toBeNull();
    expect(el.textContent).toContain('Unit cost per kg');
    component.form.patchValue({ unitCost: 60 });
    await component.save();
    expect(count).toHaveBeenCalledWith('s1', 'i1', { quantity: 2, unit: 'sack', unitCost: 60 });
  });

  it('does not ask for a unit cost when there is stock to average', async () => {
    const { fixture, el, component } = setup({ line: line() });
    await fixture.whenStable();
    component.form.patchValue({ quantity: 100 });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="count-unit-cost"]')).toBeNull();
  });

  it('puts a server field error on its control', async () => {
    count.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: { type: '/problems/validation', errors: { unit: ['Unknown unit.'] } },
      }),
    );
    const { fixture, el, component } = setup({ line: line() });
    await fixture.whenStable();
    component.form.patchValue({ quantity: 3 });
    await component.save();
    fixture.detectChanges();
    expect(close).not.toHaveBeenCalled();
    expect(el.textContent).toContain('Unknown unit.');
  });

  it('closes with reload when the stocktake is no longer open', async () => {
    count.mockRejectedValue(
      new HttpErrorResponse({
        status: 409,
        error: { type: '/problems/stocktake-not-open', stocktakeStatus: 'Posted' },
      }),
    );
    const { fixture, component } = setup({ line: line() });
    await fixture.whenStable();
    component.form.patchValue({ quantity: 3 });
    await component.save();
    expect(close).toHaveBeenCalledWith('reload');
  });
});
