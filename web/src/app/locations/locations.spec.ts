import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { PROBLEM_MESSAGES } from '../core/problems/problem-messages';
import { ConfirmDialog } from '../shared/confirm-dialog';
import { Locations } from './locations';
import { Location, LocationsService } from './locations.service';

const qc: Location = {
  id: 'l1',
  code: 'QC',
  name: 'Quezon City',
  kind: 'Physical',
  isSystem: false,
  allowNegativeStock: false,
  isActive: true,
  version: 'AAAAAAAAB9E=',
};

const sup: Location = {
  id: 'l2',
  code: 'SUP',
  name: 'Suppliers',
  kind: 'Supplier',
  isSystem: true,
  allowNegativeStock: true,
  isActive: true,
  version: 'AAAAAAAAB9F=',
};

describe('Locations', () => {
  const list = vi.fn();
  const setAllowNegativeStock = vi.fn();
  const open = vi.fn();
  const snack = vi.fn();
  const isAdmin = signal(false);

  beforeEach(async () => {
    list.mockReset();
    setAllowNegativeStock.mockReset();
    setAllowNegativeStock.mockResolvedValue(qc);
    open.mockReset();
    open.mockReturnValue({ afterClosed: () => of(true) });
    snack.mockReset();
    list.mockResolvedValue({ items: [qc, sup], page: 1, pageSize: 25, total: 2 });
    isAdmin.set(false);
    await TestBed.configureTestingModule({
      imports: [Locations],
      providers: [
        { provide: LocationsService, useValue: { list, setAllowNegativeStock } },
        { provide: MatDialog, useValue: { open } },
        { provide: MatSnackBar, useValue: { open: snack } },
        { provide: AuthService, useValue: { isAdmin } },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(Locations);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const rows = Array.from(el.querySelectorAll<HTMLElement>('.location-row'));
    const row = (code: string) => rows.find((r) => r.textContent?.includes(code))!;
    const buttons = (root: HTMLElement) =>
      Array.from(root.querySelectorAll('button:not([role="switch"])')).map(
        (b) => b.textContent?.trim() ?? '',
      );
    return { el, row, buttons };
  }

  it('loads the first page', async () => {
    await render();
    expect(list).toHaveBeenCalledOnce();
    expect(list).toHaveBeenCalledWith({ page: 1, pageSize: 25 });
  });

  it('shows a System pill on the system row only', async () => {
    const { row } = await render();
    expect(row('SUP').querySelector('app-status-pill')?.textContent).toContain('System');
    expect(row('QC').querySelector('app-status-pill')).toBeNull();
  });

  it('hides New, Edit and Deactivate from Staff', async () => {
    const { el, buttons } = await render();
    expect(buttons(el)).not.toContain('addNew location');
    expect(buttons(el)).not.toContain('Edit');
    expect(buttons(el)).not.toContain('Deactivate');
  });

  it('shows Admin New location, and Edit and Deactivate on the physical row only', async () => {
    isAdmin.set(true);
    const { el, row, buttons } = await render();
    expect(buttons(el)).toContain('addNew location');
    expect(buttons(row('QC'))).toEqual(['Edit', 'Deactivate']);
    expect(buttons(row('SUP'))).toEqual([]);
  });

  it('shows an Inactive pill and no Deactivate on an inactive physical row', async () => {
    isAdmin.set(true);
    list.mockResolvedValue({
      items: [{ ...qc, isActive: false }],
      page: 1,
      pageSize: 25,
      total: 1,
    });
    const { row, buttons } = await render();
    expect(row('QC').querySelector('app-status-pill')?.textContent).toContain('Inactive');
    expect(buttons(row('QC'))).toEqual(['Edit']);
  });

  const toggleOf = (root: HTMLElement) =>
    root.querySelector<HTMLButtonElement>('mat-slide-toggle button[role="switch"]')!;

  it('shows Admin the switch on active physical rows only', async () => {
    isAdmin.set(true);
    list.mockResolvedValue({
      items: [qc, sup, { ...qc, id: 'l3', code: 'OLD', isActive: false }],
      page: 1,
      pageSize: 25,
      total: 3,
    });
    const { row } = await render();
    expect(row('QC').querySelector('mat-slide-toggle')).not.toBeNull();
    expect(row('SUP').querySelector('mat-slide-toggle')).toBeNull();
    expect(row('OLD').querySelector('mat-slide-toggle')).toBeNull();
  });

  it('shows Staff a Negative stock allowed pill and no switch', async () => {
    list.mockResolvedValue({
      items: [{ ...qc, allowNegativeStock: true }],
      page: 1,
      pageSize: 25,
      total: 1,
    });
    const { el, row } = await render();
    const pill = row('QC').querySelector('app-status-pill')!;
    expect(pill.textContent).toContain('Negative stock allowed');
    expect(pill.getAttribute('data-tone')).toBe('warn');
    expect(el.querySelector('mat-slide-toggle')).toBeNull();
  });

  it('asks before allowing negative stock and sends nothing on cancel', async () => {
    isAdmin.set(true);
    open.mockReturnValue({ afterClosed: () => of(false) });
    const { row } = await render();
    toggleOf(row('QC')).click();
    expect(open).toHaveBeenCalledWith(
      ConfirmDialog,
      expect.objectContaining({ data: expect.objectContaining({ confirm: 'Allow' }) }),
    );
    expect(setAllowNegativeStock).not.toHaveBeenCalled();
    expect(toggleOf(row('QC')).getAttribute('aria-checked')).toBe('false');
  });

  it('allows negative stock after confirm and reloads', async () => {
    isAdmin.set(true);
    const { row } = await render();
    toggleOf(row('QC')).click();
    await vi.waitFor(() => expect(list).toHaveBeenCalledTimes(2));
    expect(setAllowNegativeStock).toHaveBeenCalledWith(qc, true);
    expect(snack).toHaveBeenCalledWith('Negative stock allowed at QC', 'OK', { duration: 4000 });
  });

  it('turns it off without a confirm', async () => {
    isAdmin.set(true);
    const on = { ...qc, allowNegativeStock: true };
    list.mockResolvedValue({ items: [on], page: 1, pageSize: 25, total: 1 });
    const { row } = await render();
    toggleOf(row('QC')).click();
    await vi.waitFor(() => expect(list).toHaveBeenCalledTimes(2));
    expect(setAllowNegativeStock).toHaveBeenCalledWith(on, false);
    expect(open).not.toHaveBeenCalled();
  });

  it('shows the problem message when turning off is refused', async () => {
    isAdmin.set(true);
    const on = { ...qc, allowNegativeStock: true };
    list.mockResolvedValue({ items: [on], page: 1, pageSize: 25, total: 1 });
    setAllowNegativeStock.mockRejectedValue(
      new HttpErrorResponse({ status: 422, error: { type: '/problems/negative-balances-exist' } }),
    );
    const { row } = await render();
    toggleOf(row('QC')).click();
    await vi.waitFor(() => expect(list).toHaveBeenCalledTimes(2));
    expect(snack).toHaveBeenCalledWith(PROBLEM_MESSAGES['negative-balances-exist'], 'OK', {
      duration: 6000,
    });
  });
});
