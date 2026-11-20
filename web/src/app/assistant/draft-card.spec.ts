import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { AssistantDraft } from './assistant.service';
import { DraftCard } from './draft-card';

const QC = { id: 'l1', code: 'QC', name: 'Quezon City branch' };
const MKT = { id: 'l2', code: 'MKT', name: 'Makati' };

function draft(over: Partial<AssistantDraft> = {}): AssistantDraft {
  return {
    type: 'receipt',
    location: QC,
    reference: 'PO-1',
    lines: [
      {
        itemId: 'i1',
        sku: 'RICE',
        itemName: 'Rice',
        baseUnit: 'kg',
        quantity: 10,
        unit: 'sack',
        unitCost: 1250,
        direction: null,
      },
    ],
    warnings: [],
    ...over,
  };
}

describe('DraftCard', () => {
  const navigate = vi.fn();
  const isAdmin = signal(true);

  beforeEach(async () => {
    navigate.mockReset();
    isAdmin.set(true);
    await TestBed.configureTestingModule({
      imports: [DraftCard],
      providers: [
        { provide: Router, useValue: { navigate } },
        { provide: AuthService, useValue: { isAdmin } },
      ],
    }).compileComponents();
  });

  function render(d: AssistantDraft) {
    const fixture = TestBed.createComponent(DraftCard);
    fixture.componentRef.setInput('draft', d);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { el, text: el.textContent ?? '' };
  }
  const button = (el: HTMLElement) => el.querySelector('button');

  it('shows a receipt in its unit', () => {
    const { text } = render(draft());
    expect(text).toContain('Receive stock');
    expect(text).toContain('Draft');
    expect(text).toContain('At QC · Quezon City branch');
    expect(text).toContain('RICE · Rice');
    expect(text).toContain('sack');
    expect(text).toContain('Reference: PO-1');
    expect(text).toContain('Not posted yet.');
  });

  it('shows the base unit when unit is null', () => {
    const d = draft();
    d.lines[0].unit = null;
    expect(render(d).text).toContain('10 kg');
  });

  it('shows a transfer from and to', () => {
    const { text } = render(
      draft({ type: 'transfer', location: null, fromLocation: QC, toLocation: MKT }),
    );
    expect(text).toContain('From QC to MKT');
  });

  it('shows an adjustment reason and direction', () => {
    const d = draft({ type: 'adjustment', reasonCode: 'Damaged' });
    d.lines[0].direction = 'remove';
    const { text } = render(d);
    expect(text).toContain('Damaged');
    expect(text).toContain('Remove');
  });

  it('shows warnings', () => {
    const { el } = render(draft({ warnings: ['Stock would go negative'] }));
    expect(el.querySelector('.warnings')?.textContent).toContain('Stock would go negative');
  });

  it('navigates with the draft in state', () => {
    const d = draft();
    const { el } = render(d);
    button(el)!.click();
    expect(navigate).toHaveBeenCalledWith(['/documents/new', 'receipt'], {
      state: { assistantDraft: d },
    });
  });

  it('hides the button for Staff on an adjustment', () => {
    isAdmin.set(false);
    const { el } = render(draft({ type: 'adjustment', reasonCode: 'Lost' }));
    expect(button(el)).toBeNull();
  });

  it('keeps the button for Staff on a receipt', () => {
    isAdmin.set(false);
    expect(button(render(draft()).el)).not.toBeNull();
  });

  it('renders markup in an item name as text', () => {
    const d = draft();
    d.lines[0].itemName = '<script>alert(1)</script>';
    const { el, text } = render(d);
    expect(text).toContain('<script>alert(1)</script>');
    expect(el.querySelector('script')).toBeNull();
  });
});
