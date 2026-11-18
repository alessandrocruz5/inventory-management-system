import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { LedgerHealth } from './ledger-health';
import {
  InvariantFailure,
  InvariantId,
  InvariantResult,
  LedgerHealth as LedgerHealthData,
  LedgerRebuild,
  LedgerService,
} from './ledger.service';

describe('LedgerHealth', () => {
  const health = vi.fn();
  const rebuild = vi.fn();
  let confirmed = true;
  let open: ReturnType<typeof vi.fn>;

  const ids: InvariantId[] = ['INV-1', 'INV-2', 'INV-3', 'INV-4', 'INV-5'];

  const passing = (id: InvariantId): InvariantResult => ({
    id,
    name: id,
    passed: true,
    failureCount: 0,
    failures: [],
  });

  const healthy = (): LedgerHealthData => ({
    checkedAt: '2026-11-16T06:14:00Z',
    healthy: true,
    invariants: ids.map(passing),
  });

  const failure = (over: Partial<InvariantFailure>): InvariantFailure => ({
    itemId: 'i1',
    sku: 'SKU-1',
    locationId: 'l1',
    locationCode: 'MAIN',
    movementId: null,
    check: 'StockValue',
    expected: 1230,
    actual: 1200,
    ...over,
  });

  const snapshot = (over = {}) => ({
    onHand: 10,
    stockValue: 1230,
    lastUnitCost: 123,
    lastMovementId: 7,
    ...over,
  });

  beforeEach(async () => {
    health.mockReset();
    rebuild.mockReset();
    confirmed = true;
    health.mockResolvedValue(healthy());
    open = vi.fn(() => ({ afterClosed: () => of(confirmed) }));
    await TestBed.configureTestingModule({
      imports: [LedgerHealth],
      providers: [
        { provide: LedgerService, useValue: { health, rebuild } },
        { provide: MatDialog, useValue: { open } },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(LedgerHealth);
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  async function clickRebuild(
    fixture: { whenStable(): Promise<unknown>; detectChanges(): void },
    el: HTMLElement,
  ) {
    const button = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Rebuild balances'),
    );
    button?.click();
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders five invariant blocks and a healthy summary without failure tables', async () => {
    const { el } = await render();
    const text = el.textContent ?? '';
    for (const expected of [
      'Balances match the ledger',
      'Stock adds up across locations',
      'No impossible balances',
      'Reversals mirror their originals',
      'Last unit cost matches the ledger',
      'All checks passed',
      'Checked 16 Nov 2026, 2:14 PM',
    ]) {
      expect(text).toContain(expected);
    }
    expect(el.querySelectorAll('.invariant')).toHaveLength(5);
    const pills = el.querySelectorAll('.invariant[data-passed="true"] app-status-pill');
    expect(pills).toHaveLength(5);
    expect(pills[0].textContent?.trim()).toBe('Passed');
    expect(text).toContain('Ran 5 checks.');
    expect(el.querySelector('table')).toBeNull();
  });

  it('shows the failing rows of a failed check', async () => {
    const data = healthy();
    data.healthy = false;
    data.invariants[0] = {
      id: 'INV-1',
      name: 'x',
      passed: false,
      failureCount: 150,
      failures: [
        failure({}),
        failure({ sku: 'SKU-2', locationCode: 'B', check: 'OnHand', expected: 5, actual: 4.5 }),
      ],
    };
    health.mockResolvedValue(data);
    const { el } = await render();
    const text = el.textContent ?? '';
    expect(text).toContain('1 check failed');
    expect(text).toContain('150 problems');
    expect(text).toContain('showing the first 100');
    expect(el.querySelector('app-status-pill[data-tone="bad"]')?.textContent).toContain('Failed');
    const rows = el.querySelectorAll('tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('SKU-1');
    expect(rows[0].textContent).toContain('MAIN');
    expect(rows[0].textContent).toContain('₱1,230.00');
    expect(rows[0].textContent).toContain('₱1,200.00');
    expect(rows[1].textContent).toContain('SKU-2');
    expect(rows[1].textContent).toContain('4.5');
    expect(rows[1].textContent).not.toContain('₱');
  });

  it('does not rebuild when the dialog is cancelled', async () => {
    confirmed = false;
    const { fixture, el } = await render();
    await clickRebuild(fixture, el);
    expect(open).toHaveBeenCalledTimes(1);
    expect(rebuild).not.toHaveBeenCalled();
    expect(health).toHaveBeenCalledTimes(1);
  });

  it('says nothing to fix and re-checks health after a clean rebuild', async () => {
    const result: LedgerRebuild = {
      rebuiltAt: '2026-11-16T06:20:00Z',
      balancesChecked: 42,
      differences: [],
    };
    rebuild.mockResolvedValue(result);
    const { fixture, el } = await render();
    await clickRebuild(fixture, el);
    expect(rebuild).toHaveBeenCalledTimes(1);
    expect(health).toHaveBeenCalledTimes(2);
    expect(el.textContent).toContain('Checked 42 balances. Nothing to fix.');
  });

  it('lists a fixed balance and flags a missing row', async () => {
    rebuild.mockResolvedValue({
      rebuiltAt: '2026-11-16T06:20:00Z',
      balancesChecked: 3,
      differences: [
        {
          itemId: 'i1',
          sku: 'SKU-9',
          locationId: 'l1',
          locationCode: 'MAIN',
          inserted: true,
          before: null,
          after: snapshot({ onHand: 6, stockValue: 750, lastUnitCost: null }),
        },
      ],
    } satisfies LedgerRebuild);
    const { fixture, el } = await render();
    await clickRebuild(fixture, el);
    const text = el.textContent ?? '';
    expect(text).toContain('Fixed 1 balances');
    const row = el.querySelector('.rebuild tbody tr')?.textContent ?? '';
    expect(row).toContain('SKU-9');
    expect(row).toContain('(row was missing)');
    expect(row).toContain('→ 6');
    expect(row).toContain('→ ₱750.00');
  });

  it('shows the mapped message when the health check fails', async () => {
    health.mockRejectedValue(
      new HttpErrorResponse({ status: 409, error: { type: '/problems/concurrency-conflict' } }),
    );
    const { el } = await render();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      'Someone else changed this record. Reload it and try again.',
    );
  });
});
