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
  };
}

interface PostStockInternals {
  form: FormGroup<{
    header: FormGroup<Record<string, FormControl>>;
    lines: FormArray<FormGroup & LineGroup>;
  }>;
  submit(): Promise<void>;
  title(): string;
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
  const onHand = vi.fn();
  const open = vi.fn();
  const paramMap = new BehaviorSubject(convertToParamMap({ type: 'receipt' }));

  beforeEach(async () => {
    post.mockReset();
    onHand.mockReset();
    open.mockReset();
    onHand.mockRejectedValue(new Error('nope'));
    open.mockReturnValue({ afterClosed: () => of(true) });
    paramMap.next(convertToParamMap({ type: 'receipt' }));
    await TestBed.configureTestingModule({
      imports: [PostStock],
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { paramMap } },
        { provide: DocumentsService, useValue: { post } },
        { provide: StockService, useValue: { onHand } },
        { provide: ItemsService, useValue: { list: vi.fn().mockResolvedValue({ items: [] }) } },
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

  it('posts a negative delta without a unit cost when an adjustment line is Remove', async () => {
    const { fixture, c } = await setup('adjustment');
    const line = fill(c, 'adjustment');
    line.controls.unitCost.setValue(3);
    line.controls.direction.setValue('remove');
    fixture.detectChanges();
    expect(line.controls.unitCost.value).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Unit cost');
    post.mockResolvedValue({ id: 'd', type: 'Adjustment' });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await c.submit();
    const body = post.mock.calls[0][2];
    expect(body.lines[0]).toEqual({ itemId: 'i1', quantityDelta: -5, unitCost: null });
  });

  it('asks before posting an outflow over on-hand, and posts nothing on cancel', async () => {
    onHand.mockResolvedValue(1);
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
});
