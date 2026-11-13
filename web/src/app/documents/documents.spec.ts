import { CUSTOM_ELEMENTS_SCHEMA, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
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
        { provide: Router, useValue: { navigate } },
      ],
    }).compileComponents();
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

  it('formats posted-at, posted-by and status', async () => {
    const fixture = TestBed.createComponent(Documents);
    await fixture.whenStable();
    const defs = component(fixture).columnDefs;
    const get = (i: number, data: DocumentSummary) =>
      (defs[i].valueGetter as (p: unknown) => string)({ data });
    expect(get(0, summary())).toBe('30 Oct 2026, 2:14 PM');
    expect(get(5, summary({ postedBy: SYSTEM_USER_ID }))).toBe('System');
    expect(get(5, summary({ postedBy: 'u-1' }))).toBe('You');
    expect(get(6, summary({ reversedByDocumentId: 'x' }))).toBe('Reversed');
    expect(get(6, summary({ reversesDocumentId: 'x' }))).toBe('Reverses another');
    expect(get(6, summary())).toBe('');
  });
});
