import { CUSTOM_ELEMENTS_SCHEMA, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AgGridAngular } from 'ag-grid-angular';
import { AuthService } from '../core/auth/auth.service';
import { Items } from './items';
import { ItemsService } from './items.service';

describe('Items', () => {
  const list = vi.fn();
  const isAdmin = signal(false);

  beforeEach(async () => {
    list.mockReset();
    list.mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0 });
    isAdmin.set(false);
    TestBed.overrideComponent(Items, {
      remove: { imports: [AgGridAngular] },
      add: { schemas: [CUSTOM_ELEMENTS_SCHEMA] },
    });
    await TestBed.configureTestingModule({
      imports: [Items],
      providers: [
        { provide: ItemsService, useValue: { list } },
        { provide: AuthService, useValue: { isAdmin } },
      ],
    }).compileComponents();
  });

  function buttonTexts(el: HTMLElement): string[] {
    return Array.from(el.querySelectorAll('button')).map((b) => b.textContent?.trim() ?? '');
  }

  it('loads the first page of active items without a search', async () => {
    const fixture = TestBed.createComponent(Items);
    await fixture.whenStable();
    expect(list).toHaveBeenCalledOnce();
    expect(list).toHaveBeenCalledWith({ active: true, page: 1, pageSize: 25 });
  });

  it('hides New, Edit and Deactivate from Staff', async () => {
    const fixture = TestBed.createComponent(Items);
    await fixture.whenStable();
    const texts = buttonTexts(fixture.nativeElement as HTMLElement);
    expect(texts).not.toContain('New item');
    expect(texts).not.toContain('Edit');
    expect(texts).not.toContain('Deactivate');
  });

  it('shows Admin all three, with Edit and Deactivate disabled until a row is selected', async () => {
    isAdmin.set(true);
    const fixture = TestBed.createComponent(Items);
    await fixture.whenStable();
    const buttons = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ) as HTMLButtonElement[];
    const byText = (t: string) => buttons.find((b) => b.textContent?.trim() === t);
    expect(byText('New item')).toBeDefined();
    expect(byText('New item')?.disabled).toBe(false);
    expect(byText('Edit')?.disabled).toBe(true);
    expect(byText('Deactivate')?.disabled).toBe(true);
  });
});
