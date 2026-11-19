import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { PageEvent } from '@angular/material/paginator';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { ItemDetail } from './item-detail';
import { ItemDetail as ItemDetailData, ItemMovement, ItemsService } from './items.service';

interface ItemDetailInternals {
  onPage(event: PageEvent): Promise<void>;
}

describe('ItemDetail', () => {
  const get = vi.fn();
  const deactivateUnit = vi.fn();
  const open = vi.fn();
  const isAdmin = signal(false);
  const movements = vi.fn();
  const params = new BehaviorSubject(convertToParamMap({ id: 'i1' }));

  const item = (over: Partial<ItemDetailData> = {}): ItemDetailData =>
    ({
      id: 'i1',
      sku: 'SKU-1',
      name: 'Widget',
      baseUnit: 'pc',
      isActive: true,
      balances: [
        {
          locationId: 'l1',
          locationCode: 'MAIN',
          locationName: 'Main',
          onHand: 10,
          stockValue: 1230,
          averageCost: 123,
          lastUnitCost: 125,
        },
      ],
      units: [
        { code: 'case', factor: 12, isActive: true },
        { code: 'old', factor: 0.5, isActive: false },
      ],
      ...over,
    }) as ItemDetailData;

  const movement = (over: Partial<ItemMovement>): ItemMovement => ({
    movementId: 1,
    documentId: 'd6',
    documentType: 'Receipt',
    lineNo: 1,
    postedAt: '2026-10-30T06:14:00Z',
    postedBy: '00000000-0000-0000-0000-000000000000',
    direction: 'In',
    locationId: 'l1',
    locationCode: 'MAIN',
    locationName: 'Main',
    counterpartLocationId: 'l2',
    counterpartLocationCode: 'SUP',
    counterpartLocationName: 'Supplier',
    quantityChange: 6,
    valueChange: 750,
    runningOnHand: 10,
    runningValue: 1230,
    ...over,
  });

  const page = (items: ItemMovement[]) => ({ items, page: 1, pageSize: 25, total: items.length });

  const twoMovements = () => [
    movement({}),
    movement({
      movementId: 2,
      documentId: 'd5',
      documentType: 'Transfer',
      direction: 'Out',
      counterpartLocationCode: 'B',
      counterpartLocationName: 'Branch B',
      quantityChange: -4,
      valueChange: -480,
      runningOnHand: 6,
      runningValue: 720,
      postedBy: 'u-1',
    }),
  ];

  beforeEach(async () => {
    get.mockReset();
    deactivateUnit.mockReset();
    open.mockReset();
    isAdmin.set(false);
    movements.mockReset();
    get.mockResolvedValue(item());
    movements.mockResolvedValue(page(twoMovements()));
    params.next(convertToParamMap({ id: 'i1' }));
    await TestBed.configureTestingModule({
      imports: [ItemDetail],
      providers: [
        provideRouter([]),
        { provide: ItemsService, useValue: { get, movements, deactivateUnit } },
        { provide: AuthService, useValue: { user: signal({ id: 'u-1' }), isAdmin } },
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    })
      .overrideProvider(MatDialog, { useValue: { open } })
      .compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(ItemDetail);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  it('renders the item, its balances and its history', async () => {
    const { el } = await render();
    const text = el.textContent ?? '';
    for (const expected of [
      'SKU-1',
      'Widget',
      '₱123.00',
      '₱1,230.00',
      'Received',
      'Moved',
      'from SUP',
      'to B',
      '+6',
      '-4',
      '+₱750.00',
      'System',
      'You',
      '30 Oct 2026, 2:14 PM',
    ]) {
      expect(text).toContain(expected);
    }
    expect(text).not.toContain('10.0000');
  });

  it('links each history row to its document', async () => {
    const { el } = await render();
    const hrefs = Array.from(el.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toContain('/documents/d6');
    expect(hrefs).toContain('/documents/d5');
  });

  it('shows empty states', async () => {
    get.mockResolvedValue(item({ balances: [] }));
    movements.mockResolvedValue(page([]));
    const { el } = await render();
    expect(el.textContent).toContain('No stock at any location yet.');
    expect(el.textContent).toContain('No movements yet.');
  });

  it('shows Inactive for an inactive item', async () => {
    get.mockResolvedValue(item({ isActive: false }));
    const { el } = await render();
    expect(el.textContent).toContain('Inactive');
  });

  it('shows the not-found message with a link back on 404', async () => {
    get.mockRejectedValue(
      new HttpErrorResponse({ status: 404, error: { type: '/problems/not-found' } }),
    );
    const { el } = await render();
    expect(el.textContent).toContain("That record doesn't exist, or you can't see it.");
    expect(el.querySelector('a')?.getAttribute('href')).toBe('/stock');
  });

  it('pages the history', async () => {
    const { fixture } = await render();
    await (fixture.componentInstance as unknown as ItemDetailInternals).onPage({
      pageIndex: 1,
      pageSize: 25,
      length: 30,
    });
    expect(movements).toHaveBeenLastCalledWith('i1', 2, 25);
    expect(get).toHaveBeenCalledTimes(1);
  });

  it('reloads from page 1 when the id changes', async () => {
    const { fixture } = await render();
    await (fixture.componentInstance as unknown as ItemDetailInternals).onPage({
      pageIndex: 1,
      pageSize: 25,
      length: 30,
    });
    params.next(convertToParamMap({ id: 'i2' }));
    await fixture.whenStable();
    expect(get).toHaveBeenLastCalledWith('i2');
    expect(movements).toHaveBeenLastCalledWith('i2', 1, 25);
  });

  it('shows the name as the title and the summed on-hand in the meta line', async () => {
    get.mockResolvedValue(
      item({
        balances: [
          { ...item().balances[0], onHand: 0.1 },
          { ...item().balances[0], locationId: 'l2', locationCode: 'B', onHand: 0.2 },
        ],
      }),
    );
    const { el } = await render();
    expect(el.querySelector('h1')?.textContent?.trim()).toBe('Widget');
    const meta = el.querySelector('.meta')?.textContent ?? '';
    expect(meta).toContain('On hand');
    expect(meta).toContain('0.3');
    expect(meta).not.toContain('0.30000');
  });

  it('keeps the back link to /stock in the loaded state', async () => {
    const { el } = await render();
    expect(el.querySelector('a')?.getAttribute('href')).toBe('/stock');
  });

  it('lists the units as "1 code = factor base" with an Inactive pill and no controls for Staff', async () => {
    const { el } = await render();
    const text = el.querySelector('.units-table')?.textContent ?? '';
    expect(text).toContain('1 case = 12 pc');
    expect(text).toContain('1 old = 0.5 pc');
    expect(text).toContain('Inactive');
    expect(el.textContent).not.toContain('Add unit');
    expect(el.querySelector('[aria-label="Deactivate case"]')).toBeNull();
  });

  it('shows the empty state when there are no units', async () => {
    get.mockResolvedValue(item({ units: [] }));
    const { el } = await render();
    expect(el.textContent).toContain('No other units. Stock is counted in pc.');
  });

  it('gives an Admin Add unit and Deactivate on active rows only', async () => {
    isAdmin.set(true);
    const { el } = await render();
    expect(el.textContent).toContain('Add unit');
    expect(el.querySelector('[aria-label="Deactivate case"]')).not.toBeNull();
    expect(el.querySelector('[aria-label="Deactivate old"]')).toBeNull();
  });

  it('reloads the item after a unit is added', async () => {
    isAdmin.set(true);
    open.mockReturnValue({ afterClosed: () => of({ code: 'sack', factor: 25, isActive: true }) });
    const { fixture, el } = await render();
    const add = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Add unit'),
    )!;
    add.click();
    await fixture.whenStable();
    expect(open).toHaveBeenCalledTimes(1);
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('deactivates a unit only after confirmation, then reloads', async () => {
    isAdmin.set(true);
    deactivateUnit.mockResolvedValue({ code: 'case', factor: 12, isActive: false });
    open.mockReturnValueOnce({ afterClosed: () => of(false) });
    const { fixture, el } = await render();
    const button = () => el.querySelector<HTMLButtonElement>('[aria-label="Deactivate case"]')!;
    button().click();
    await fixture.whenStable();
    expect(deactivateUnit).not.toHaveBeenCalled();
    open.mockReturnValueOnce({ afterClosed: () => of(true) });
    button().click();
    await fixture.whenStable();
    expect(open.mock.calls[1][1].data.message).toContain('Deactivate case?');
    expect(deactivateUnit).toHaveBeenCalledWith('i1', 'case');
    expect(get).toHaveBeenCalledTimes(2);
  });
});
