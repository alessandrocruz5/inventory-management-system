import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { BehaviorSubject, of } from 'rxjs';
import { PROBLEM_MESSAGES } from '../core/problems/problem-messages';
import { AuthService } from '../core/auth/auth.service';
import { DocumentDetail } from './document-detail';
import { DocumentDetail as DocumentDetailData, DocumentsService } from './documents.service';

describe('DocumentDetail', () => {
  const get = vi.fn();
  const reverse = vi.fn();
  const isAdmin = signal(false);
  let confirmed = true;
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
    reverse.mockReset();
    isAdmin.set(false);
    confirmed = true;
    get.mockResolvedValue(doc());
    params.next(convertToParamMap({ id: 'd1' }));
    await TestBed.configureTestingModule({
      imports: [DocumentDetail],
      providers: [
        provideRouter([]),
        { provide: DocumentsService, useValue: { get, reverse } },
        { provide: AuthService, useValue: { user: signal({ id: 'u-1' }), isAdmin } },
        {
          provide: MatDialog,
          useValue: { open: vi.fn(() => ({ afterClosed: () => of(confirmed) })) },
        },
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

  const button = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Reverse');

  async function click(fixture: ComponentFixture<DocumentDetail>) {
    button(fixture.nativeElement)?.click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function renderFixture() {
    const fixture = TestBed.createComponent(DocumentDetail);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('shows Reverse to an admin on an open receipt', async () => {
    isAdmin.set(true);
    expect(button(await render())).toBeTruthy();
  });

  it('hides Reverse for staff, reversals and reversed documents', async () => {
    expect(button(await render())).toBeUndefined();
    isAdmin.set(true);
    get.mockResolvedValue(doc({ type: 'Reversal' }));
    expect(button(await render())).toBeUndefined();
    get.mockResolvedValue(doc({ reversedByDocumentId: 'r1' }));
    expect(button(await render())).toBeUndefined();
  });

  it('reverses on confirm, reloads and hides the button', async () => {
    isAdmin.set(true);
    reverse.mockResolvedValue({});
    const fixture = await renderFixture();
    get.mockResolvedValue(doc({ reversedByDocumentId: 'r1' }));
    await click(fixture);
    expect(reverse).toHaveBeenCalledTimes(1);
    expect(reverse).toHaveBeenCalledWith('d1', expect.any(String));
    expect(get).toHaveBeenCalledTimes(2);
    expect(button(fixture.nativeElement)).toBeUndefined();
  });

  it('does nothing when the dialog is cancelled', async () => {
    isAdmin.set(true);
    confirmed = false;
    await click(await renderFixture());
    expect(reverse).not.toHaveBeenCalled();
  });

  it('shows reversal-not-latest with a link to the blocking document', async () => {
    isAdmin.set(true);
    reverse.mockRejectedValue(
      new HttpErrorResponse({
        status: 409,
        error: { type: '/problems/reversal-not-latest', blockingDocumentId: 'b1' },
      }),
    );
    const fixture = await renderFixture();
    await click(fixture);
    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain(PROBLEM_MESSAGES['reversal-not-latest']);
    expect(alert?.querySelector('a')?.getAttribute('href')).toBe('/documents/b1');
  });

  it('reuses the key after a failed attempt', async () => {
    isAdmin.set(true);
    reverse.mockRejectedValue(new HttpErrorResponse({ status: 0 }));
    const fixture = await renderFixture();
    await click(fixture);
    await click(fixture);
    expect(reverse).toHaveBeenCalledTimes(2);
    expect(reverse.mock.calls[1][1]).toBe(reverse.mock.calls[0][1]);
  });

  it('links to the reversal on already-reversed and reloads', async () => {
    isAdmin.set(true);
    reverse.mockRejectedValue(
      new HttpErrorResponse({
        status: 409,
        error: { type: '/problems/already-reversed', reversalDocumentId: 'r9' },
      }),
    );
    const fixture = await renderFixture();
    await click(fixture);
    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.querySelector('a')?.getAttribute('href')).toBe('/documents/r9');
    expect(get).toHaveBeenCalledTimes(2);
  });
});
