import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../core/auth/auth.service';
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
  const isAdmin = signal(false);

  beforeEach(async () => {
    list.mockReset();
    list.mockResolvedValue({ items: [qc, sup], page: 1, pageSize: 25, total: 2 });
    isAdmin.set(false);
    await TestBed.configureTestingModule({
      imports: [Locations],
      providers: [
        { provide: LocationsService, useValue: { list } },
        { provide: AuthService, useValue: { isAdmin } },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(Locations);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const rows = Array.from(el.querySelectorAll<HTMLElement>('mat-list-item'));
    const row = (code: string) => rows.find((r) => r.textContent?.includes(code))!;
    const buttons = (root: HTMLElement) =>
      Array.from(root.querySelectorAll('button')).map((b) => b.textContent?.trim() ?? '');
    return { el, row, buttons };
  }

  it('loads the first page', async () => {
    await render();
    expect(list).toHaveBeenCalledOnce();
    expect(list).toHaveBeenCalledWith({ page: 1, pageSize: 25 });
  });

  it('shows a System chip on the system row only', async () => {
    const { row } = await render();
    expect(row('SUP').querySelector('mat-chip')?.textContent).toContain('System');
    expect(row('QC').querySelector('mat-chip')).toBeNull();
  });

  it('hides New, Edit and Deactivate from Staff', async () => {
    const { el, buttons } = await render();
    expect(buttons(el)).not.toContain('New location');
    expect(buttons(el)).not.toContain('Edit');
    expect(buttons(el)).not.toContain('Deactivate');
  });

  it('shows Admin New location, and Edit and Deactivate on the physical row only', async () => {
    isAdmin.set(true);
    const { el, row, buttons } = await render();
    expect(buttons(el)).toContain('New location');
    expect(buttons(row('QC'))).toEqual(['Edit', 'Deactivate']);
    expect(buttons(row('SUP'))).toEqual([]);
  });

  it('shows an Inactive chip and no Deactivate on an inactive physical row', async () => {
    isAdmin.set(true);
    list.mockResolvedValue({
      items: [{ ...qc, isActive: false }],
      page: 1,
      pageSize: 25,
      total: 1,
    });
    const { row, buttons } = await render();
    expect(row('QC').querySelector('mat-chip')?.textContent).toContain('Inactive');
    expect(buttons(row('QC'))).toEqual(['Edit']);
  });
});
