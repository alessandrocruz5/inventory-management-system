import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormArray, FormControl, FormGroup } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { ItemsService } from '../items/items.service';
import { LocationsService } from '../locations/locations.service';
import { StockService } from '../stock/stock.service';
import { DocumentsService } from './documents.service';
import { PostStock } from './post-stock';

interface LineGroup {
  controls: {
    item: FormControl;
    quantity: FormControl<number | null>;
    unitCost: FormControl<number | null>;
    direction: FormControl<string>;
    unit: FormControl<string>;
  };
}

interface PostStockInternals {
  form: FormGroup<{
    header: FormGroup<Record<string, FormControl>>;
    lines: FormArray<FormGroup & LineGroup>;
  }>;
  submit(): Promise<void>;
  title(): string;
  subtitle(): string;
  totalLabel(): string;
  message(): string | null;
}

const ITEM = {
  id: 'i1',
  sku: 'SKU-1',
  name: 'Widget',
  baseUnit: 'pc',
  isActive: true,
  version: 'v',
};

describe('PostStock', () => {
  const post = vi.fn();
  const balance = vi.fn();
  const open = vi.fn();
  const getItem = vi.fn();
  const paramMap = new BehaviorSubject(convertToParamMap({ type: 'receipt' }));

  beforeEach(async () => {
    post.mockReset();
    balance.mockReset();
    open.mockReset();
    getItem.mockReset();
    getItem.mockResolvedValue({ ...ITEM, balances: [], units: [] });
    balance.mockRejectedValue(new Error('nope'));
    open.mockReturnValue({ afterClosed: () => of(true) });
    paramMap.next(convertToParamMap({ type: 'receipt' }));
    await TestBed.configureTestingModule({
      imports: [PostStock],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap } },
        { provide: DocumentsService, useValue: { post } },
        { provide: StockService, useValue: { balance } },
        {
          provide: ItemsService,
          useValue: { list: vi.fn().mockResolvedValue({ items: [] }), get: getItem },
        },
        {
          provide: LocationsService,
          useValue: {
            list: vi.fn().mockResolvedValue({
              items: [{ id: 'l1', code: 'MAIN', name: 'Main', isSystem: false, isActive: true }],
            }),
          },
        },
        { provide: AuthService, useValue: { isAdmin: signal(true), ensureLoaded: vi.fn() } },
        { provide: MatDialog, useValue: { open } },
        { provide: MatSnackBar, useValue: { open: vi.fn() } },
      ],
    }).compileComponents();
  });

  async function setup(
    type = 'receipt',
  ): Promise<{ fixture: ComponentFixture<PostStock>; c: PostStockInternals }> {
    paramMap.next(convertToParamMap({ type }));
    const fixture = TestBed.createComponent(PostStock);
    await fixture.whenStable();
    return { fixture, c: fixture.componentInstance as unknown as PostStockInternals };
  }

  function fill(c: PostStockInternals, type: string, quantity = 5): FormGroup & LineGroup {
    const h = c.form.controls.header.controls;
    h['locationId']?.setValue('l1');
    if (type === 'transfer') {
      h['fromLocationId'].setValue('l1');
      h['toLocationId'].setValue('l2');
    }
    if (type === 'adjustment') {
      h['reasonCode'].setValue('Lost');
    }
    const line = c.form.controls.lines.at(0);
    line.controls.item.setValue(ITEM);
    line.controls.quantity.setValue(quantity);
    if (type === 'receipt') {
      line.controls.unitCost.setValue(10);
    }
    return line;
  }

  const insufficient = () =>
    new HttpErrorResponse({
      status: 422,
      error: {
        type: '/problems/insufficient-stock',
        itemId: 'i1',
        locationId: 'l1',
        available: 2,
        requested: 5,
      },
    });

  it('keeps the idempotency key across an insufficient-stock retry', async () => {
    const { c } = await setup('issue');
    const line = fill(c, 'issue');
    post.mockRejectedValueOnce(insufficient()).mockResolvedValueOnce({ id: 'd', type: 'Issue' });
    await c.submit();
    expect(line.controls.quantity.errors?.['server']).toBe('Only 2 pc available; you asked for 5.');
    line.controls.quantity.setValue(2);
    await c.submit();
    expect(post).toHaveBeenCalledTimes(2);
    expect(post.mock.calls[1][1]).toBe(post.mock.calls[0][1]);
  });

  it('renews the key and links the original after idempotency-key-reused', async () => {
    const { fixture, c } = await setup('issue');
    fill(c, 'issue');
    post
      .mockRejectedValueOnce(
        new HttpErrorResponse({
          status: 409,
          error: { type: '/problems/idempotency-key-reused', originalDocumentId: 'd1' },
        }),
      )
      .mockResolvedValueOnce({ id: 'd', type: 'Issue' });
    await c.submit();
    fixture.detectChanges();
    const a = (fixture.nativeElement as HTMLElement).querySelector('a');
    expect(a?.getAttribute('href')).toBe('/documents/d1');
    await c.submit();
    expect(post.mock.calls[1][1]).not.toBe(post.mock.calls[0][1]);
  });

  it('keeps the key after a network error', async () => {
    const { c } = await setup('issue');
    fill(c, 'issue');
    post
      .mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }))
      .mockResolvedValueOnce({ id: 'd', type: 'Issue' });
    await c.submit();
    await c.submit();
    expect(post).toHaveBeenCalledTimes(2);
    expect(post.mock.calls[1][1]).toBe(post.mock.calls[0][1]);
  });

  it('resets lines, key and title when :type changes', async () => {
    const { fixture, c } = await setup('receipt');
    fill(c, 'receipt');
    c.form.controls.lines.push(c.form.controls.lines.at(0));
    paramMap.next(convertToParamMap({ type: 'issue' }));
    fixture.detectChanges();
    expect(c.form.controls.lines.length).toBe(1);
    expect(c.form.controls.lines.at(0).controls.quantity.value).toBeNull();
    expect(c.title()).toBe('Use or sell stock');
    fill(c, 'issue');
    post.mockResolvedValue({ id: 'd', type: 'Issue' });
    await c.submit();
    paramMap.next(convertToParamMap({ type: 'receipt' }));
    fill(c, 'receipt');
    await c.submit();
    expect(post.mock.calls[1][1]).not.toBe(post.mock.calls[0][1]);
  });

  it('disables submit while a post is pending', async () => {
    const { fixture, c } = await setup('issue');
    fill(c, 'issue');
    let resolve!: (v: unknown) => void;
    post.mockReturnValue(new Promise((r) => (resolve = r)));
    const pending = c.submit();
    fixture.detectChanges();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      'button[type=submit]',
    ) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    resolve({ id: 'd', type: 'Issue' });
    await pending;
  });

  it('navigates to the posted document', async () => {
    const { c } = await setup('issue');
    fill(c, 'issue');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    post.mockResolvedValue({ id: 'd9', type: 'Issue' });
    await c.submit();
    expect(navigate).toHaveBeenCalledWith(['/documents', 'd9']);
  });

  it('never reports a committed post as failed when navigation fails', async () => {
    const { c } = await setup('issue');
    fill(c, 'issue');
    vi.spyOn(TestBed.inject(Router), 'navigate').mockRejectedValue(new Error('chunk failed'));
    post.mockResolvedValue({ id: 'd9', type: 'Issue' });
    await c.submit();
    expect(c.message()).toBeNull();
    expect(TestBed.inject(MatSnackBar).open).toHaveBeenCalledWith('Posted', 'OK', {
      duration: 4000,
    });
  });

  it('posts a negative delta without a unit cost when an adjustment line is Remove', async () => {
    const { fixture, c } = await setup('adjustment');
    const line = fill(c, 'adjustment');
    line.controls.unitCost.setValue(3);
    line.controls.direction.setValue('remove');
    fixture.detectChanges();
    expect(line.controls.unitCost.value).toBeNull();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('label[for="post-cost-0"]'),
    ).toBeNull();
    post.mockResolvedValue({ id: 'd', type: 'Adjustment' });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await c.submit();
    const body = post.mock.calls[0][2];
    expect(body.lines[0]).toEqual({ itemId: 'i1', quantityDelta: -5, unitCost: null });
  });

  it('asks before posting an outflow over on-hand, and posts nothing on cancel', async () => {
    balance.mockResolvedValue({ onHand: 1, averageCost: 4 });
    open.mockReturnValue({ afterClosed: () => of(false) });
    const { fixture, c } = await setup('issue');
    const line = c.form.controls.lines.at(0);
    line.controls.item.setValue(ITEM);
    line.controls.quantity.setValue(5);
    c.form.controls.header.controls['locationId'].setValue('l1');
    await fixture.whenStable();
    await c.submit();
    expect(open).toHaveBeenCalledOnce();
    expect(post).not.toHaveBeenCalled();
  });

  it('marks the quantity control on a 400 for lines[0].quantityDelta', async () => {
    const { c } = await setup('adjustment');
    const line = fill(c, 'adjustment');
    post.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: {
          type: '/problems/validation',
          errors: { 'lines[0].quantityDelta': ['Too big.'] },
        },
      }),
    );
    await c.submit();
    expect(line.controls.quantity.errors?.['server']).toBe('Too big.');
  });

  it('shows the subtitle and total label for each type', async () => {
    const { fixture, c } = await setup('issue');
    expect(c.subtitle()).toBe('Record stock that was sold or used up.');
    expect(c.totalLabel()).toBe('Value at average cost');
    const el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    expect(el.querySelector('.page-subtitle')?.textContent).toContain('sold or used up');
    expect(el.querySelector('.post-total__label')?.textContent).toContain('Value at average cost');
    const receipt = await setup('receipt');
    expect(receipt.c.totalLabel()).toBe('Total cost');
  });

  it('shows the running total in the sticky bar', async () => {
    const { fixture, c } = await setup('receipt');
    const line = c.form.controls.lines.at(0);
    line.controls.quantity.setValue(2);
    line.controls.unitCost.setValue(10);
    fixture.detectChanges();
    const bar = (fixture.nativeElement as HTMLElement).querySelector('.post-bar');
    expect(bar?.textContent).toContain('₱20.00');
  });

  it('shows the on-hand hint and the negative warning for an issue line', async () => {
    balance.mockResolvedValue({ onHand: 5, averageCost: 4 });
    const { fixture, c } = await setup('issue');
    const line = c.form.controls.lines.at(0);
    c.form.controls.header.controls['locationId'].setValue('l1');
    line.controls.item.setValue(ITEM);
    line.controls.quantity.setValue(2);
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.hint')?.textContent).toContain('On hand at MAIN: 5 pc');
    expect(el.querySelector('.post-bar')?.textContent).toContain('₱8.00');
    line.controls.quantity.setValue(9);
    fixture.detectChanges();
    expect(el.querySelector('.hint')?.textContent).toContain(
      'Only 5 pc on hand at MAIN. This will take the balance negative.',
    );
  });

  describe('units', () => {
    const RICE = { ...ITEM, id: 'i2', sku: 'RICE', baseUnit: 'kg' };
    const SACK = { code: 'sack', factor: 25, isActive: true };
    const OLD = { code: 'bag', factor: 5, isActive: false };

    function chooseRice(c: PostStockInternals): FormGroup & LineGroup {
      const line = c.form.controls.lines.at(0);
      line.controls.item.setValue(RICE);
      return line;
    }

    it('hides the Unit select for an item with no units', async () => {
      const { fixture, c } = await setup('receipt');
      chooseRice(c);
      await fixture.whenStable();
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).querySelector('#post-unit-0')).toBeNull();
      expect(getItem).toHaveBeenCalledExactlyOnceWith('i2');
    });

    it('shows the Unit select with the active units, and posts the chosen unit', async () => {
      getItem.mockResolvedValue({ ...RICE, balances: [], units: [SACK, OLD] });
      post.mockResolvedValue({ id: 'd', type: 'Receipt' });
      const { fixture, c } = await setup('receipt');
      c.form.controls.header.controls['locationId'].setValue('l1');
      const line = chooseRice(c);
      line.controls.quantity.setValue(1);
      line.controls.unitCost.setValue(1250);
      await fixture.whenStable();
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('#post-unit-0')).not.toBeNull();
      expect(el.querySelector('label[for="post-unit-0"]')?.textContent).toContain('Unit');

      line.controls.unit.setValue('sack');
      fixture.detectChanges();
      expect(el.querySelector('label[for="post-cost-0"]')?.textContent).toContain('per sack');
      expect(el.querySelector('.post-bar')?.textContent).toContain('₱1,250.00');
      await c.submit();
      expect(post.mock.calls[0][2].lines).toEqual([
        { itemId: 'i2', quantity: 1, unitCost: 1250, unit: 'sack' },
      ]);
    });

    it('omits the unit when the base unit is chosen', async () => {
      getItem.mockResolvedValue({ ...RICE, balances: [], units: [SACK] });
      post.mockResolvedValue({ id: 'd', type: 'Receipt' });
      const { fixture, c } = await setup('receipt');
      c.form.controls.header.controls['locationId'].setValue('l1');
      const line = chooseRice(c);
      line.controls.quantity.setValue(25);
      line.controls.unitCost.setValue(50);
      await fixture.whenStable();
      await c.submit();
      expect(post.mock.calls[0][2].lines).toEqual([{ itemId: 'i2', quantity: 25, unitCost: 50 }]);
    });

    it('fetches an item once and warns in base units', async () => {
      getItem.mockResolvedValue({ ...RICE, balances: [], units: [SACK] });
      balance.mockResolvedValue({ onHand: 40, averageCost: 50 });
      const { fixture, c } = await setup('issue');
      c.form.controls.header.controls['locationId'].setValue('l1');
      const line = chooseRice(c);
      line.controls.item.setValue(RICE);
      line.controls.quantity.setValue(2);
      await fixture.whenStable();
      line.controls.unit.setValue('sack');
      fixture.detectChanges();
      expect(getItem).toHaveBeenCalledTimes(1);
      const el = fixture.nativeElement as HTMLElement;
      // 2 sacks = 50 kg > 40 kg on hand; the value is 50 kg x 50.
      expect(el.querySelector('.hint')?.textContent).toContain('Only 40 kg on hand at MAIN');
      expect(el.querySelector('.post-bar')?.textContent).toContain('₱2,500.00');
    });

    it('marks the Unit select on a 400 for lines[0].unit', async () => {
      getItem.mockResolvedValue({ ...RICE, balances: [], units: [SACK] });
      const { fixture, c } = await setup('receipt');
      c.form.controls.header.controls['locationId'].setValue('l1');
      const line = chooseRice(c);
      line.controls.quantity.setValue(1);
      line.controls.unitCost.setValue(1);
      await fixture.whenStable();
      line.controls.unit.setValue('sack');
      post.mockRejectedValue(
        new HttpErrorResponse({
          status: 400,
          error: { type: '/problems/validation', errors: { 'lines[0].unit': ['Unknown unit.'] } },
        }),
      );
      await c.submit();
      expect(line.controls.unit.errors?.['server']).toBe('Unknown unit.');
    });
  });

  it('labels the fields with the label-above pattern', async () => {
    const { fixture } = await setup('receipt');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('mat-select')?.getAttribute('aria-labelledby')).toContain(
      'post-location-label',
    );
    expect(el.querySelector('label[for="post-reference"]')).not.toBeNull();
    expect(el.querySelector('label[for="post-item-0"]')).not.toBeNull();
    expect(el.querySelector('button[aria-label="Remove line"]')).not.toBeNull();
  });
});
