import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { LocationDialog } from './location-dialog';
import { Location, LocationsService } from './locations.service';

const existing: Location = {
  id: 'l1',
  code: 'QC',
  name: 'Quezon City',
  kind: 'Physical',
  isSystem: false,
  allowNegativeStock: false,
  isActive: true,
  version: 'AAAAAAAAB9E=',
};

describe('LocationDialog', () => {
  const create = vi.fn();
  const update = vi.fn();
  const close = vi.fn();

  function setup(location: Location | null) {
    TestBed.configureTestingModule({
      imports: [LocationDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { location } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: LocationsService, useValue: { create, update } },
      ],
    });
    const fixture = TestBed.createComponent(LocationDialog);
    const el = fixture.nativeElement as HTMLElement;
    const inputs = () => Array.from(el.querySelectorAll<HTMLInputElement>('input'));
    const save = () => el.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    const type = async (code: string, name: string) => {
      [code, name].forEach((value, i) => {
        const input = inputs()[i];
        input.value = value;
        input.dispatchEvent(new Event('input'));
      });
      await fixture.whenStable();
    };
    return { fixture, el, inputs, save, type };
  }

  beforeEach(() => {
    create.mockReset();
    update.mockReset();
    close.mockReset();
  });

  it('disables Save when the form is empty', async () => {
    const { fixture, el, save } = setup(null);
    await fixture.whenStable();
    expect(save().disabled).toBe(true);
    expect(el.querySelector('h2')?.textContent).toContain('New location');
  });

  it('creates and closes with the result', async () => {
    create.mockResolvedValue(existing);
    const { fixture, save, type } = setup(null);
    await fixture.whenStable();
    await type('QC', 'Quezon City');
    expect(save().disabled).toBe(false);
    save().click();
    await fixture.whenStable();
    expect(create).toHaveBeenCalledWith({ code: 'QC', name: 'Quezon City' });
    expect(close).toHaveBeenCalledWith(existing);
  });

  it('prefills when editing and calls update(location, value)', async () => {
    const saved = { ...existing, name: 'QC Warehouse' };
    update.mockResolvedValue(saved);
    const { fixture, el, inputs, save, type } = setup(existing);
    await fixture.whenStable();
    expect(el.querySelector('h2')?.textContent).toContain('Edit location');
    expect(inputs().map((i) => i.value)).toEqual(['QC', 'Quezon City']);
    await type('QC', 'QC Warehouse');
    save().click();
    await fixture.whenStable();
    expect(update).toHaveBeenCalledWith(existing, { code: 'QC', name: 'QC Warehouse' });
    expect(close).toHaveBeenCalledWith(saved);
  });

  it('shows a field error from a 400 and stays open', async () => {
    create.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: {
          type: '/problems/validation',
          errors: { code: ['A location with this code already exists.'] },
        },
      }),
    );
    const { fixture, el, save, type } = setup(null);
    await fixture.whenStable();
    await type('QC', 'Quezon City');
    save().click();
    await fixture.whenStable();
    expect(el.querySelector('mat-error')?.textContent).toContain(
      'A location with this code already exists.',
    );
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(close).not.toHaveBeenCalled();
  });
});
