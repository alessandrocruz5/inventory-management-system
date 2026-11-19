import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { PROBLEM_MESSAGES } from '../core/problems/problem-messages';
import { ItemUnitDialog } from './item-unit-dialog';
import { ItemsService } from './items.service';

describe('ItemUnitDialog', () => {
  const addUnit = vi.fn();
  const close = vi.fn();

  function setup() {
    TestBed.configureTestingModule({
      imports: [ItemUnitDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { itemId: 'i1', baseUnit: 'pc' } },
        { provide: MatDialogRef, useValue: { close } },
        { provide: ItemsService, useValue: { addUnit } },
      ],
    });
    const fixture = TestBed.createComponent(ItemUnitDialog);
    const el = fixture.nativeElement as HTMLElement;
    const submit = () => el.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    const type = async (code: string, factor: string) => {
      await fixture.whenStable();
      fixture.detectChanges();
      const inputs = Array.from(el.querySelectorAll<HTMLInputElement>('input'));
      [code, factor].forEach((value, i) => {
        inputs[i].value = value;
        inputs[i].dispatchEvent(new Event('input'));
      });
      await fixture.whenStable();
      fixture.detectChanges();
    };
    return { fixture, el, submit, type };
  }

  beforeEach(() => {
    addUnit.mockReset();
    close.mockReset();
  });

  it('disables Add unit until the form is valid and shows the hint', async () => {
    const { fixture, el, submit, type } = setup();
    await fixture.whenStable();
    expect(submit().disabled).toBe(true);
    await type('case', '12');
    expect(submit().disabled).toBe(false);
    expect(el.textContent).toContain('How many pc in one case?');
  });

  it('rejects a zero factor and more than 4 decimals', async () => {
    const { submit, type } = setup();
    await type('case', '0');
    expect(submit().disabled).toBe(true);
    await type('case', '1.23456');
    expect(submit().disabled).toBe(true);
    await type('case', '0.5');
    expect(submit().disabled).toBe(false);
  });

  it('adds the unit and closes with it', async () => {
    const unit = { code: 'case', factor: 12, isActive: true };
    addUnit.mockResolvedValue(unit);
    const { fixture, submit, type } = setup();
    await type('case', '12');
    submit().click();
    await fixture.whenStable();
    expect(addUnit).toHaveBeenCalledWith('i1', { code: 'case', factor: 12 });
    expect(close).toHaveBeenCalledWith(unit);
  });

  it('puts a server field error on its control and stays open', async () => {
    addUnit.mockRejectedValue(
      new HttpErrorResponse({
        status: 400,
        error: {
          type: '/problems/validation',
          errors: { code: ['This item already has a unit with that code.'] },
        },
      }),
    );
    const { fixture, el, submit, type } = setup();
    await type('case', '12');
    submit().click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('mat-error')?.textContent).toContain(
      'This item already has a unit with that code.',
    );
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(close).not.toHaveBeenCalled();
  });

  it('shows other problems in a message line', async () => {
    addUnit.mockRejectedValue(
      new HttpErrorResponse({ status: 409, error: { type: '/problems/concurrency-conflict' } }),
    );
    const { fixture, el, submit, type } = setup();
    await type('case', '12');
    submit().click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      PROBLEM_MESSAGES['concurrency-conflict'],
    );
    expect(close).not.toHaveBeenCalled();
  });
});
