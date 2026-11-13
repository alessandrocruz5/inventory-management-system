import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { DocumentDetail } from './document-detail';
import { DocumentDetail as DocumentDetailData, DocumentsService } from './documents.service';

describe('DocumentDetail', () => {
  const get = vi.fn();
  const params = new BehaviorSubject(convertToParamMap({ id: 'd1' }));

  const doc = (over: Partial<DocumentDetailData> = {}): DocumentDetailData => ({
    id: 'd1',
    type: 'Receipt',
    reasonCode: null,
    reference: 'PO-1',
    notes: null,
    postedAt: '2026-10-30T06:14:00Z',
    postedBy: 'u-1',
    reversesDocumentId: null,
    reversedByDocumentId: null,
    lines: [
      {
        movementId: 1,
        lineNo: 1,
        itemId: 'i',
        sku: 'SKU-1',
        itemName: 'Widget',
        baseUnit: 'pc',
        fromLocationId: 'a',
        fromLocationCode: 'SUP',
        fromLocationName: 'Supplier',
        toLocationId: 'b',
        toLocationCode: 'MAIN',
        toLocationName: 'Main',
        quantity: 10,
        unitCost: 100,
        totalCost: 1000,
        reversesMovementId: null,
      },
    ],
    ...over,
  });

  beforeEach(async () => {
    get.mockReset();
    get.mockResolvedValue(doc());
    params.next(convertToParamMap({ id: 'd1' }));
    await TestBed.configureTestingModule({
      imports: [DocumentDetail],
      providers: [
        provideRouter([]),
        { provide: DocumentsService, useValue: { get } },
        { provide: AuthService, useValue: { user: signal({ id: 'u-1' }) } },
        { provide: ActivatedRoute, useValue: { paramMap: params } },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(DocumentDetail);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the type label and the lines', async () => {
    const el = await render();
    expect(el.querySelector('h1')?.textContent).toContain('Received');
    const text = el.textContent ?? '';
    expect(text).toContain('SKU-1 · Widget');
    expect(text).toContain('10 pc');
    expect(text).toContain('₱100.00');
    expect(text).toContain('₱1,000.00');
    expect(text).not.toContain('10.0');
  });

  it('renders both reversal links when set', async () => {
    get.mockResolvedValue(doc({ reversedByDocumentId: 'r1', reversesDocumentId: 'o1' }));
    const el = await render();
    const hrefs = Array.from(el.querySelectorAll('a')).map((a) => a.getAttribute('href'));
    expect(hrefs).toContain('/documents/r1');
    expect(hrefs).toContain('/documents/o1');
  });

  it('renders no reversal links when null', async () => {
    const el = await render();
    expect(el.textContent).not.toContain('Reversed by');
    expect(el.textContent).not.toContain('Reverses');
  });

  it('shows the not-found message with a link back on 404', async () => {
    get.mockRejectedValue(
      new HttpErrorResponse({ status: 404, error: { type: '/problems/not-found' } }),
    );
    const el = await render();
    expect(el.textContent).toContain("That record doesn't exist, or you can't see it.");
    expect(el.querySelector('a')?.getAttribute('href')).toBe('/documents');
  });

  it('reloads when the id changes', async () => {
    const fixture = TestBed.createComponent(DocumentDetail);
    await fixture.whenStable();
    params.next(convertToParamMap({ id: 'd2' }));
    await fixture.whenStable();
    expect(get).toHaveBeenCalledTimes(2);
    expect(get).toHaveBeenLastCalledWith('d2');
  });
});
