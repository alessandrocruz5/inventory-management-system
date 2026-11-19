import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialog, MatDialogRef } from '@angular/material/dialog';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { LocationsService } from '../locations/locations.service';
import { StartStocktakeDialog } from './start-stocktake-dialog';
import { Stocktakes } from './stocktakes';
import { StocktakesService } from './stocktakes.service';

const location = {
  id: 'loc-1',
  code: 'KITCHEN',
  name: 'Kitchen',
  kind: 'Physical',
  isSystem: false,
  allowNegativeStock: false,
  isActive: true,
  version: 'v1',
};

function summary(id: string, status: string) {
  return {
    id,
    locationId: 'loc-1',
    locationCode: 'KITCHEN',
    locationName: 'Kitchen',
    status,
    notes: null,
    createdAt: '2026-11-19T02:00:00Z',
    createdBy: 'u1',
    lineCount: 4,
    countedCount: 1,
    postedDocumentId: null,
  };
}

describe('Stocktakes', () => {
  const list = vi.fn();
  const open = vi.fn();
  const locationsList = vi.fn();

  beforeEach(async () => {
    list.mockReset();
    open.mockReset();
    locationsList.mockReset();
    locationsList.mockResolvedValue({ items: [location], page: 1, pageSize: 100, total: 1 });
    await TestBed.configureTestingModule({
      imports: [Stocktakes],
      providers: [
        provideRouter([]),
        { provide: StocktakesService, useValue: { list } },
        { provide: LocationsService, useValue: { list: locationsList } },
      ],
    }).compileComponents();
    TestBed.overrideProvider(MatDialog, { useValue: { open } });
  });

  async function render(items: ReturnType<typeof summary>[]) {
    list.mockResolvedValue({ items, page: 1, pageSize: 25, total: items.length });
    const fixture = TestBed.createComponent(Stocktakes);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders rows with plain-word status labels and progress', async () => {
    const el = await render([
      summary('a', 'Draft'),
      summary('b', 'Posting'),
      summary('c', 'Posted'),
      summary('d', 'Cancelled'),
    ]);
    const pills = Array.from(el.querySelectorAll('app-status-pill')).map((p) =>
      p.textContent?.trim(),
    );
    expect(pills).toEqual(['Counting', 'Posting…', 'Posted', 'Cancelled']);
    expect(el.textContent).toContain('1 of 4 counted');
    expect(el.querySelector('a.count-row')?.getAttribute('href')).toBe('/stocktakes/a');
  });

  it('shows the empty state', async () => {
    const el = await render([]);
    expect(el.textContent).toContain(
      "No counts yet. Start one to check what's really on the shelf.",
    );
  });

  it('navigates to the stocktake the start dialog returns', async () => {
    open.mockReturnValue({ afterClosed: () => of('new-id') });
    const el = await render([]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const start = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Start a count'),
    );
    start!.click();
    expect(open).toHaveBeenCalledWith(StartStocktakeDialog);
    expect(navigate).toHaveBeenCalledWith(['/stocktakes', 'new-id']);
  });
});

describe('StartStocktakeDialog', () => {
  const start = vi.fn();
  const close = vi.fn();

  function setup() {
    TestBed.configureTestingModule({
      imports: [StartStocktakeDialog],
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: null },
        { provide: MatDialogRef, useValue: { close } },
        { provide: StocktakesService, useValue: { start } },
        {
          provide: LocationsService,
          useValue: {
            list: vi
              .fn()
              .mockResolvedValue({ items: [location], page: 1, pageSize: 100, total: 1 }),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(StartStocktakeDialog);
    const el = fixture.nativeElement as HTMLElement;
    const component = fixture.componentInstance as unknown as {
      form: { setValue(v: { locationId: string; notes: string }): void };
      start(): Promise<void>;
    };
    return { fixture, el, component };
  }

  beforeEach(() => {
    start.mockReset();
    close.mockReset();
  });

  it('posts the location and notes, then closes with the new id', async () => {
    start.mockResolvedValue({ id: 'new-id' });
    const { fixture, component } = setup();
    await fixture.whenStable();
    component.form.setValue({ locationId: 'loc-1', notes: ' Month end ' });
    await component.start();
    expect(start).toHaveBeenCalledWith({ locationId: 'loc-1', notes: 'Month end' });
    expect(close).toHaveBeenCalledWith('new-id');
  });

  it('offers "Open it" on stocktake-open-exists', async () => {
    start.mockRejectedValue(
      new HttpErrorResponse({
        status: 409,
        error: { type: '/problems/stocktake-open-exists', stocktakeId: 'open-id' },
      }),
    );
    const { fixture, el, component } = setup();
    await fixture.whenStable();
    component.form.setValue({ locationId: 'loc-1', notes: '' });
    await component.start();
    fixture.detectChanges();
    expect(el.textContent).toContain('already has a stocktake in progress');
    const openIt = Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Open it',
    );
    expect(openIt).toBeDefined();
    openIt!.click();
    expect(close).toHaveBeenCalledWith('open-id');
  });
});
