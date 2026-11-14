import { HttpErrorResponse } from '@angular/common/http';
import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef } from 'ag-grid-community';
import { problemMessage } from '../core/problems/problem-messages';
import { LocationsService } from '../locations/locations.service';
import { Stock } from './stock';
import { StockRow, StockService } from './stock.service';

interface StockInternals {
  onLocation(id: string | null): void;
  totalsRow(): unknown;
  locations(): { id: string }[];
  columnDefs: ColDef<StockRow>[];
}

describe('Stock', () => {
  const list = vi.fn();
  const listLocations = vi.fn();
  const open = vi.fn();

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
      ],
    }).compileComponents();
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
    expect(fmt(2, 10)).toBe('10');
    expect(fmt(2, undefined)).toBe('');
    expect(fmt(5, 1234.5)).toBe('₱1,234.50');
    expect(fmt(4, null)).toBe('—');
    expect(fmt(4, 1234.5)).toBe('₱1,234.50');
  });

  function component(fixture: { componentInstance: Stock }) {
    return fixture.componentInstance as unknown as StockInternals;
  }
});
