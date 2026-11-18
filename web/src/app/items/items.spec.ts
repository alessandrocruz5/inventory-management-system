import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { ConfirmDialog } from '../shared/confirm-dialog';
import { Items } from './items';
import { ItemsService } from './items.service';

describe('Items', () => {
  const list = vi.fn();
  const isAdmin = signal(false);
  const open = vi.fn();

  const fixtureItems = [
    {
      id: 'id-1',
      sku: 'CANDLE-WHT',
      name: 'White candle',
      baseUnit: 'pc',
      isActive: true,
      version: 'v1',
    },
    {
      id: 'id-2',
      sku: 'OLD-WICK',
      name: 'Old wick',
      baseUnit: 'pc',
      isActive: false,
      version: 'v1',
    },
  ];

  beforeEach(async () => {
    list.mockReset();
    open.mockReset();
    list.mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0 });
    isAdmin.set(false);
    await TestBed.configureTestingModule({
      imports: [Items],
      providers: [
        provideRouter([]),
        { provide: ItemsService, useValue: { list } },
        { provide: AuthService, useValue: { isAdmin } },
      ],
    }).compileComponents();
    TestBed.overrideProvider(MatDialog, { useValue: { open } });
  });

  async function render(rows = fixtureItems) {
    list.mockResolvedValue({ items: rows, page: 1, pageSize: 25, total: rows.length });
    const fixture = TestBed.createComponent(Items);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('loads the first page of active items without a search', async () => {
    const fixture = TestBed.createComponent(Items);
    await fixture.whenStable();
    expect(list).toHaveBeenCalledOnce();
    expect(list).toHaveBeenCalledWith({ active: true, page: 1, pageSize: 25 });
  });

  it('hides New, Edit and Deactivate from Staff', async () => {
    const el = await render();
    const texts = Array.from(el.querySelectorAll('button')).map((b) => b.textContent?.trim());
    expect(texts).not.toContain('New item');
    expect(el.querySelector('button[aria-label^="Edit "]')).toBeNull();
    expect(el.querySelector('button[aria-label^="Deactivate "]')).toBeNull();
  });

  it('shows Admin New item, per-row Edit, and Deactivate for active rows only', async () => {
    isAdmin.set(true);
    const el = await render();
    const newItem = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('New item'),
    );
    expect(newItem).toBeDefined();
    expect(newItem?.disabled).toBe(false);
    expect(el.querySelector('button[aria-label="Edit CANDLE-WHT"]')).not.toBeNull();
    expect(el.querySelector('button[aria-label="Edit OLD-WICK"]')).not.toBeNull();
    expect(el.querySelector('button[aria-label="Deactivate CANDLE-WHT"]')).not.toBeNull();
    expect(el.querySelector('button[aria-label="Deactivate OLD-WICK"]')).toBeNull();
  });

  it('links each row to its item', async () => {
    const el = await render();
    const hrefs = Array.from(el.querySelectorAll('a.item-main')).map((a) => a.getAttribute('href'));
    expect(hrefs).toEqual(['/items/id-1', '/items/id-2']);
  });

  it('asks for confirmation naming the SKU when Deactivate is clicked', async () => {
    isAdmin.set(true);
    open.mockReturnValue({ afterClosed: () => of(false) });
    const el = await render();
    (el.querySelector('button[aria-label="Deactivate CANDLE-WHT"]') as HTMLButtonElement).click();
    expect(open).toHaveBeenCalledOnce();
    const [component, config] = open.mock.calls[0];
    expect(component).toBe(ConfirmDialog);
    expect(config.data.message).toContain('CANDLE-WHT');
  });
});
