import { BreakpointObserver } from '@angular/cdk/layout';
import { CUSTOM_ELEMENTS_SCHEMA, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef } from 'ag-grid-community';
import { AuthService } from '../core/auth/auth.service';
import { SYSTEM_USER_ID } from '../shared/format';
import { Documents } from './documents';
import { DocumentSummary, DocumentsService } from './documents.service';

interface DocumentsInternals {
  onType(type: string | null): void;
  onFrom(value: string): void;
  onTo(value: string): void;
  onRowClicked(row: DocumentSummary | undefined): void;
  columnDefs: ColDef<DocumentSummary>[];
}

describe('Documents', () => {
  const list = vi.fn();
  const navigate = vi.fn();
  const user = signal<{ id: string } | null>({ id: 'u-1' });
  let narrow = false;

  const summary = (over: Partial<DocumentSummary> = {}): DocumentSummary => ({
    id: 'd1',
    type: 'Issue',
    reasonCode: null,
    reference: null,
    postedAt: '2026-10-30T06:14:00Z',
    postedBy: 'u-1',
    lineCount: 1,
    reversesDocumentId: null,
    reversedByDocumentId: null,
    ...over,
  });

  beforeEach(async () => {
    list.mockReset();
    navigate.mockReset();
    narrow = false;
    list.mockResolvedValue({ items: [summary()], page: 1, pageSize: 25, total: 1 });
    TestBed.overrideComponent(Documents, {
      remove: { imports: [AgGridAngular] },
      add: { schemas: [CUSTOM_ELEMENTS_SCHEMA] },
    });
    await TestBed.configureTestingModule({
      imports: [Documents],
      providers: [
        { provide: DocumentsService, useValue: { list } },
        { provide: AuthService, useValue: { user } },
        provideRouter([]),
        {
          provide: BreakpointObserver,
          useFactory: () => ({
            observe: () => of({ matches: narrow, breakpoints: {} }),
            isMatched: () => narrow,
          }),
        },
      ],
    }).compileComponents();
    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation(navigate);
  });

  function component(fixture: { componentInstance: Documents }) {
    return fixture.componentInstance as unknown as DocumentsInternals;
  }

  it('loads page 1 with no filters', async () => {
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    expect(list).toHaveBeenCalledOnce();
    expect(list).toHaveBeenCalledWith({ page: 1, pageSize: 25 });
  });

  it('reloads from page 1 with the chosen type', async () => {
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    component(fixture).onType('Issue');
    await fixture.whenStable();
    expect(list).toHaveBeenLastCalledWith({ type: 'Issue', page: 1, pageSize: 25 });
  });

  it('turns Manila days into a half-open UTC range', async () => {
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    component(fixture).onFrom('2026-10-30');
    component(fixture).onTo('2026-10-30');
    await fixture.whenStable();
    expect(list).toHaveBeenLastCalledWith({
      postedFrom: '2026-10-29T16:00:00.000Z',
      postedTo: '2026-10-30T16:00:00.000Z',
      page: 1,
      pageSize: 25,
    });
  });

  it('navigates to the document on row click', async () => {
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    component(fixture).onRowClicked(summary({ id: 'abc' }));
    expect(navigate).toHaveBeenCalledWith(['/documents', 'abc']);
  });

  it('formats posted-at, reason and posted-by', async () => {
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    const defs = component(fixture).columnDefs;
    const get = (i: number, data: DocumentSummary) =>
      (defs[i].valueGetter as (p: unknown) => string)({ data });
    expect(get(0, summary())).toBe('30 Oct 2026, 2:14 PM');
    expect(get(3, summary())).toBe('—');
    expect(get(3, summary({ reasonCode: 'Damaged' }))).toBe('Damaged');
    expect(get(5, summary({ postedBy: SYSTEM_USER_ID }))).toBe('System');
    expect(get(5, summary({ postedBy: 'u-1' }))).toBe('You');
    const reference = defs[2].valueFormatter as (p: unknown) => string;
    expect(reference({ value: null })).toBe('—');
    expect(reference({ value: 'PO-1' })).toBe('PO-1');
  });

  it('derives the Status column as Posted, Reversed or Reverses another', async () => {
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    const status = component(fixture).columnDefs[6];
    const get = (data: DocumentSummary) =>
      (status.valueGetter as (p: unknown) => { label: string })({ data }).label;
    expect(get(summary())).toBe('Posted');
    expect(get(summary({ reversedByDocumentId: 'x' }))).toBe('Reversed');
    expect(get(summary({ reversesDocumentId: 'x' }))).toBe('Reverses another');
    expect(status.sortable).toBe(false);
  });

  it('renders one stacked row per document when narrow', async () => {
    narrow = true;
    list.mockResolvedValue({
      items: [
        summary({ id: 'd1', reference: 'REF', lineCount: 2 }),
        summary({ id: 'd2', reference: null, lineCount: 1 }),
      ],
      page: 1,
      pageSize: 25,
      total: 2,
    });
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const links = Array.from(el.querySelectorAll<HTMLAnchorElement>('a.stacked-row'));
    expect(links).toHaveLength(2);
    expect(links[0].getAttribute('href')).toBe('/documents/d1');
    expect(links[0].textContent).toContain('Used or sold');
    expect(links[0].textContent?.replace(/\s+/g, ' ')).toContain('REF · 2 lines');
    expect(links[1].textContent?.replace(/\s+/g, ' ')).toContain('1 line');
    expect(links[1].textContent).not.toContain('1 lines');
    expect(el.querySelector('ag-grid-angular')).toBeNull();
  });

  it('renders the grid and not the stacked list on desktop', async () => {
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('ag-grid-angular')).not.toBeNull();
    expect(el.querySelector('a.stacked-row')).toBeNull();
  });

  it('labels the Type select and the date inputs', async () => {
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('mat-select')?.getAttribute('aria-labelledby')?.trim()).toBe(
      'documents-type-label',
    );
    expect(el.querySelector('#documents-type-label')?.textContent?.trim()).toBe('Type');
    expect(el.querySelector('label[for="documents-from"]')?.textContent?.trim()).toBe('From');
    expect(el.querySelector('label[for="documents-to"]')?.textContent?.trim()).toBe('To');
    expect(el.querySelector('input#documents-from')).not.toBeNull();
    expect(el.querySelector('input#documents-to')).not.toBeNull();
  });
});
