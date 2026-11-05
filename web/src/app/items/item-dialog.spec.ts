import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { PROBLEM_MESSAGES } from '../core/problems/problem-messages';
import { ItemDialog } from './item-dialog';
import { Item, ItemsService } from './items.service';

const existing: Item = {
  id: 'a1',
  sku: 'RICE-1',
  name: 'Rice',
  baseUnit: 'kg',
  isActive: true,
  version: 'AAAAAAAAB9E=',
};

describe('ItemDialog', () => {
  const create = vi.fn();
  const update = vi.fn();
  const close = vi.fn();

  function setup(item: Item | null) {
    TestBed.configureTestingModule({
      imports: [ItemDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { item } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: ItemsService, useValue: { create, update } },
      ],
    });
    const fixture = TestBed.createComponent(ItemDialog);
    const el = fixture.nativeElement as HTMLElement;
    const inputs = () => Array.from(el.querySelectorAll<HTMLInputElement>('input'));
    const save = () => el.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    const type = async (sku: string, name: string, baseUnit: string) => {
      [sku, name, baseUnit].forEach((value, i) => {
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
    expect(el.querySelector('h2')?.textContent).toContain('New item');
  });

  it('creates and closes with the result', async () => {
    create.mockResolvedValue(existing);
    const { fixture, save, type } = setup(null);
    await fixture.whenStable();
    await type('RICE-1', 'Rice', 'kg');
    expect(save().disabled).toBe(false);
    save().click();
    await fixture.whenStable();
    expect(create).toHaveBeenCalledWith({ sku: 'RICE-1', name: 'Rice', baseUnit: 'kg' });
    expect(close).toHaveBeenCalledWith(existing);
  });

  it('prefills when editing and calls update(item, value)', async () => {
    const saved = { ...existing, name: 'Rice 2' };
    update.mockResolvedValue(saved);
    const { fixture, el, inputs, save, type } = setup(existing);
    await fixture.whenStable();
    expect(el.querySelector('h2')?.textContent).toContain('Edit item');
    expect(inputs().map((i) => i.value)).toEqual(['RICE-1', 'Rice', 'kg']);
    await type('RICE-1', 'Rice 2', 'kg');
    save().click();
    await fixture.whenStable();
    expect(update).toHaveBeenCalledWith(existing, {
      sku: 'RICE-1',
      name: 'Rice 2',
      baseUnit: 'kg',
    });
    expect(close).toHaveBeenCalledWith(saved);
  });

  it('shows a field error from a 400 and stays open', async () => {
    create.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: {
          type: '/problems/validation',
          errors: { sku: ['An item with this SKU already exists.'] },
        },
      }),
    );
    const { fixture, el, save, type } = setup(null);
    await fixture.whenStable();
    await type('RICE-1', 'Rice', 'kg');
    save().click();
    await fixture.whenStable();
    expect(el.querySelector('mat-error')?.textContent).toContain(
      'An item with this SKU already exists.',
    );
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(close).not.toHaveBeenCalled();
  });

  it('shows the concurrency-conflict message on a 409', async () => {
    update.mockRejectedValue(
      new HttpErrorResponse({
        status: 409,
        error: { type: '/problems/concurrency-conflict' },
      }),
    );
    const { fixture, el, save } = setup(existing);
    await fixture.whenStable();
    save().click();
    await fixture.whenStable();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      PROBLEM_MESSAGES['concurrency-conflict'],
    );
    expect(close).not.toHaveBeenCalled();
    expect(save().disabled).toBe(false);
  });
});
