import { BreakpointObserver } from '@angular/cdk/layout';
import { HttpErrorResponse } from '@angular/common/http';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef } from 'ag-grid-community';
import { problemMessage } from '../core/problems/problem-messages';
import { LocationsService } from '../locations/locations.service';
import { Stock } from './stock';
import { StockRow, StockService } from './stock.service';

interface StockInternals {
  onLocation(id: string | null): void;
  onRowClicked(row: Partial<StockRow> | undefined): void;
  totalsRow(): unknown;
  locations(): { id: string }[];
  columnDefs: ColDef<StockRow>[];
  subtitle(): string;
}

describe('Stock', () => {
  const list = vi.fn();
  const listLocations = vi.fn();
  const open = vi.fn();
  const navigate = vi.fn();
  let narrow = false;

  const row = (onHand: number) => ({
    itemId: 'i',
    sku: 'SKU-1',
    itemName: 'Widget',
    baseUnit: 'pc',
    locationId: 'l1',
    locationCode: 'MAIN',
    locationName: 'Main',
    onHand,
    stockValue: onHand,
    averageCost: 1,
    lastUnitCost: 1,
  });

  const loc = (id: string, isSystem: boolean) => ({
    id,
    code: id.toUpperCase(),
    name: `Name ${id}`,
    isSystem,
    isActive: true,
  });

  beforeEach(async () => {
    list.mockReset();
    listLocations.mockReset();
    open.mockReset();
    navigate.mockReset();
    narrow = false;
    list.mockResolvedValue({
      items: [row(10), row(20)],
      page: 1,
      pageSize: 25,
      total: 2,
      totals: { stockValue: 999 },
    });
    listLocations.mockResolvedValue({
      items: [loc('a', false), loc('sys', true)],
      page: 1,
      pageSize: 100,
      total: 2,
    });
    TestBed.overrideComponent(Stock, {
      remove: { imports: [AgGridAngular] },
      add: { schemas: [CUSTOM_ELEMENTS_SCHEMA] },
    });
    await TestBed.configureTestingModule({
      imports: [Stock],
      providers: [
        { provide: StockService, useValue: { list } },
        { provide: LocationsService, useValue: { list: listLocations } },
        { provide: MatSnackBar, useValue: { open } },
        provideRouter([]),
        {
          provide: BreakpointObserver,
          useFactory: () => ({
            observe: () => of({ matches: narrow, breakpoints: {} }),
            isMatched: () => narrow,
          }),
        },
      ],
    }).compileComponents();
    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation(navigate);
  });

  it('loads page 1 with pageSize 25 and no location', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    expect(list).toHaveBeenCalledOnce();
    expect(list).toHaveBeenCalledWith({ page: 1, pageSize: 25 });
  });

  it('reloads from page 1 with the chosen location', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    component(fixture).onLocation('a');
    await fixture.whenStable();
    expect(list).toHaveBeenLastCalledWith({ locationId: 'a', page: 1, pageSize: 25 });
  });

  it('uses the API totals and labels the pinned row Total', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    const c = component(fixture);
    expect(c.totalsRow()).toEqual([{ stockValue: 999 }]);
    const getter = c.columnDefs[0].valueGetter as (p: unknown) => unknown;
    expect(getter({ node: { rowPinned: 'bottom' }, data: undefined })).toBe('Total');
    expect(getter({ node: {}, data: row(10) })).toBe('SKU-1 · Widget');
  });

  it('offers only physical locations', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    expect(
      component(fixture)
        .locations()
        .map((l) => l.id),
    ).toEqual(['a']);
  });

  it('shows a snack bar when loading fails', async () => {
    list.mockRejectedValue(new HttpErrorResponse({ status: 500, error: null }));
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    expect(open).toHaveBeenCalledWith(problemMessage(null), 'OK', { duration: 6000 });
  });

  it('formats quantity and money on the column defs', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    const defs = component(fixture).columnDefs;
    const fmt = (i: number, value: unknown) =>
      (defs[i].valueFormatter as (p: unknown) => string)({ value });
    expect(fmt(3, 10)).toBe('10');
    expect(fmt(3, undefined)).toBe('');
    expect(fmt(6, 1234.5)).toBe('₱1,234.50');
    expect(fmt(5, null)).toBe('—');
    expect(fmt(5, 1234.5)).toBe('₱1,234.50');
  });

  it('marks negative quantity and value cells', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    const defs = component(fixture).columnDefs;
    const qty = defs.find((d) => d.field === 'onHand');
    const value = defs.find((d) => d.field === 'stockValue');
    const avg = defs.find((d) => d.field === 'averageCost');
    const rule = (def: ColDef<StockRow> | undefined, v: unknown) =>
      (def?.cellClassRules?.['stock-negative'] as ((p: unknown) => boolean) | undefined)?.({
        value: v,
      });
    expect(rule(qty, -2)).toBe(true);
    expect(rule(qty, 0)).toBe(false);
    expect(rule(qty, 5)).toBe(false);
    expect(rule(qty, undefined)).toBe(false);
    expect(rule(value, -2)).toBe(true);
    expect(rule(value, 0)).toBe(false);
    expect(rule(value, 5)).toBe(false);
    expect(rule(value, undefined)).toBe(false);
    expect(avg?.cellClassRules).toBeUndefined();
  });

  it('navigates to the item on row click', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    component(fixture).onRowClicked(row(5));
    expect(navigate).toHaveBeenCalledWith(['/items', 'i']);
  });

  it('ignores the totals row', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    component(fixture).onRowClicked({ stockValue: 100 });
    component(fixture).onRowClicked(undefined);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('reads "2 balances across 1 location" in the subtitle', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    expect(component(fixture).subtitle()).toBe('2 balances across 1 location');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.page-subtitle')?.textContent?.trim()).toBe(
      '2 balances across 1 location',
    );
  });

  it('derives the Status column from on-hand quantity', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    const status = component(fixture).columnDefs.find((d) => d.colId === 'status');
    const getter = status?.valueGetter as (p: unknown) => { label: string } | undefined;
    expect(getter({ data: row(5) })?.label).toBe('In stock');
    expect(getter({ data: row(0) })?.label).toBe('Out of stock');
    expect(getter({ data: row(-1) })?.label).toBe('Negative');
    expect(getter({ data: undefined })).toBeUndefined();
    expect(status?.sortable).toBe(false);
  });

  it('renders the stacked list instead of the grid when narrow', async () => {
    narrow = true;
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('a.stacked-row'));
    expect(links).toHaveLength(2);
    expect(links[0].getAttribute('href')).toBe('/items/i');
    expect(el.querySelector('ag-grid-angular')).toBeNull();
    expect(el.querySelector('.page-actions')).toBeNull();
  });

  it('renders the grid and the Receive stock action on desktop', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('ag-grid-angular')).not.toBeNull();
    expect(el.querySelector('a.stacked-row')).toBeNull();
    expect(el.querySelector('.page-actions a')?.getAttribute('href')).toBe(
      '/documents/new/receipt',
    );
  });

  it('labels the location select with the visible Location label', async () => {
    const fixture = TestBed.createComponent(Stock);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const select = el.querySelector('mat-select');
    expect(select?.getAttribute('aria-labelledby')?.trim()).toBe('stock-location-label');
    expect(el.querySelector('#stock-location-label')?.textContent?.trim()).toBe('Location');
  });

  function component(fixture: { componentInstance: Stock }) {
    return fixture.componentInstance as unknown as StockInternals;
  }
});
