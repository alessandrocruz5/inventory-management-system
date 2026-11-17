import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AllCommunityModule, ColDef, ICellRendererParams, ModuleRegistry } from 'ag-grid-community';
import { AgGridAngular, ICellRendererAngularComp } from 'ag-grid-angular';
import { AuthService } from '../core/auth/auth.service';
import { problemMessage } from '../core/problems/problem-messages';
import {
  DocumentType,
  documentStatus,
  documentTypeLabel,
  formatManilaDateTime,
  manilaDayRange,
  postedByLabel,
  reasonLabel,
} from '../shared/format';
import { appGridTheme } from '../shared/grid-theme';
import { injectIsNarrow } from '../shared/layout';
import { StatusPill } from '../shared/status-pill';
import { StatusPillCell } from '../shared/status-pill-cell';
import { DocumentSummary, DocumentsService } from './documents.service';

ModuleRegistry.registerModules([AllCommunityModule]);

const TYPES: readonly DocumentType[] = ['Receipt', 'Issue', 'Transfer', 'Adjustment', 'Reversal'];

const TYPE_ICONS: Record<DocumentType, string> = {
  Receipt: 'south_west',
  Issue: 'north_east',
  Transfer: 'swap_horiz',
  Adjustment: 'tune',
  Reversal: 'undo',
};

/** Type cell: a muted 18 px icon beside the type label. Label comes from the typed enum, not user input. */
@Component({
  selector: 'app-document-type-cell',
  imports: [MatIconModule],
  template: `
    @if (type(); as t) {
      <mat-icon class="type-icon" aria-hidden="true">{{ icon(t) }}</mat-icon>
      <span class="type-label">{{ label(t) }}</span>
    }
  `,
  styles: `
    :host {
      display: flex;
      align-items: center;
      gap: 8px;
      height: 100%;
    }
    .type-icon {
      width: 18px;
      height: 18px;
      font-size: 18px;
      color: var(--ims-muted);
    }
    .type-label {
      font-weight: 500;
    }
  `,
})
class DocumentTypeCell implements ICellRendererAngularComp {
  protected readonly type = signal<DocumentType | null>(null);
  protected readonly icon = (t: DocumentType) => TYPE_ICONS[t];
  protected readonly label = documentTypeLabel;

  agInit(params: ICellRendererParams<DocumentSummary>): void {
    this.type.set(params.data?.type ?? null);
  }

  refresh(params: ICellRendererParams<DocumentSummary>): boolean {
    this.type.set(params.data?.type ?? null);
    return true;
  }
}

@Component({
  selector: 'app-documents',
  imports: [
    AgGridAngular,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatSelectModule,
    RouterLink,
    StatusPill,
  ],
  host: { class: 'page' },
  templateUrl: './documents.html',
  styleUrl: './documents.scss',
})
export class Documents {
  private readonly documentsService = inject(DocumentsService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly theme = appGridTheme;
  protected readonly isNarrow = injectIsNarrow();
  protected readonly types = TYPES;
  protected readonly typeLabel = documentTypeLabel;
  protected readonly typeIcon = (t: DocumentType) => TYPE_ICONS[t];
  protected readonly formatPostedAt = formatManilaDateTime;
  protected readonly documentStatus = documentStatus;
  protected readonly skeletonRows = [1, 2, 3, 4, 5];

  protected readonly columnDefs: ColDef<DocumentSummary>[] = [
    {
      colId: 'posted',
      headerName: 'Posted',
      flex: 1,
      minWidth: 170,
      cellClass: 'secondary tabular',
      valueGetter: (params) => (params.data ? formatManilaDateTime(params.data.postedAt) : ''),
    },
    {
      colId: 'type',
      headerName: 'Type',
      flex: 1,
      minWidth: 150,
      valueGetter: (params) => (params.data ? documentTypeLabel(params.data.type) : ''),
      cellRenderer: DocumentTypeCell,
    },
    {
      field: 'reference',
      headerName: 'Reference',
      flex: 1,
      minWidth: 120,
      cellClass: 'mono',
      cellStyle: { fontSize: '13px' },
      valueFormatter: (params) => params.value || '—',
    },
    {
      colId: 'reason',
      headerName: 'Reason',
      flex: 1,
      minWidth: 120,
      valueGetter: (params) =>
        params.data?.reasonCode ? reasonLabel(params.data.reasonCode) : '—',
    },
    { field: 'lineCount', headerName: 'Lines', flex: 1, minWidth: 80, type: 'rightAligned' },
    {
      colId: 'postedBy',
      headerName: 'Posted by',
      flex: 1,
      minWidth: 120,
      valueGetter: (params) => (params.data ? this.postedBy(params.data) : ''),
    },
    {
      colId: 'status',
      headerName: 'Status',
      flex: 1,
      minWidth: 150,
      sortable: false,
      valueGetter: (params) => (params.data ? documentStatus(params.data) : undefined),
      cellRenderer: StatusPillCell,
    },
  ];

  protected readonly pageSizeOptions = [25, 50, 100];

  protected readonly rows = signal<DocumentSummary[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(false);
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = signal(25);
  protected readonly type = signal<DocumentType | null>(null);
  protected readonly from = signal<string | null>(null);
  protected readonly to = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  protected postedBy(doc: DocumentSummary): string {
    return postedByLabel(doc.postedBy, this.auth.user()?.id);
  }

  protected onType(type: DocumentType | null): void {
    this.type.set(type);
    this.refilter();
  }

  protected onFrom(value: string): void {
    this.from.set(value || null);
    this.refilter();
  }

  protected onTo(value: string): void {
    this.to.set(value || null);
    this.refilter();
  }

  protected onPage(event: PageEvent): void {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    void this.load();
  }

  protected onRowClicked(row: DocumentSummary | undefined): void {
    if (row) {
      void this.router.navigate(['/documents', row.id]);
    }
  }

  private refilter(): void {
    this.pageIndex.set(0);
    void this.load();
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const type = this.type();
      const result = await this.documentsService.list({
        ...(type ? { type } : {}),
        ...manilaDayRange(this.from(), this.to()),
        page: this.pageIndex() + 1,
        pageSize: this.pageSize(),
      });
      this.rows.set(result.items);
      this.total.set(result.total);
    } catch (err) {
      this.snackBar.open(
        problemMessage(err instanceof HttpErrorResponse ? err.error : null),
        'OK',
        { duration: 6000 },
      );
    } finally {
      this.loading.set(false);
    }
  }
}
