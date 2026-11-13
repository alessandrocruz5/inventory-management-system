import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AllCommunityModule, ColDef, ModuleRegistry, themeQuartz } from 'ag-grid-community';
import { AgGridAngular } from 'ag-grid-angular';
import { AuthService } from '../core/auth/auth.service';
import { problemMessage } from '../core/problems/problem-messages';
import {
  DocumentType,
  documentTypeLabel,
  formatManilaDateTime,
  manilaDayRange,
  postedByLabel,
  reasonLabel,
} from '../shared/format';
import { DocumentSummary, DocumentsService } from './documents.service';

ModuleRegistry.registerModules([AllCommunityModule]);

const TYPES: readonly DocumentType[] = ['Receipt', 'Issue', 'Transfer', 'Adjustment', 'Reversal'];

@Component({
  selector: 'app-documents',
  imports: [AgGridAngular, MatFormFieldModule, MatInputModule, MatPaginatorModule, MatSelectModule],
  templateUrl: './documents.html',
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 16px;
      padding: 16px;
    }
    h1 {
      margin: 0;
      font: var(--mat-sys-headline-medium);
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: 8px 16px;
    }
    .filter {
      flex: 1 1 200px;
      max-width: 260px;
    }
    @media (max-width: 599px) {
      .filters {
        flex-direction: column;
      }
      .filter {
        max-width: none;
      }
    }
    .grid {
      height: 480px;
      width: 100%;
    }
  `,
})
export class Documents {
  private readonly documentsService = inject(DocumentsService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly theme = themeQuartz;
  protected readonly types = TYPES;
  protected readonly typeLabel = documentTypeLabel;

  protected readonly columnDefs: ColDef<DocumentSummary>[] = [
    {
      headerName: 'Posted',
      minWidth: 170,
      valueGetter: (params) => (params.data ? formatManilaDateTime(params.data.postedAt) : ''),
    },
    {
      headerName: 'Type',
      valueGetter: (params) => (params.data ? documentTypeLabel(params.data.type) : ''),
    },
    {
      headerName: 'Reason',
      valueGetter: (params) => (params.data?.reasonCode ? reasonLabel(params.data.reasonCode) : ''),
    },
    { field: 'reference', headerName: 'Reference' },
    { field: 'lineCount', headerName: 'Lines', type: 'rightAligned' },
    {
      headerName: 'Posted by',
      valueGetter: (params) =>
        params.data ? postedByLabel(params.data.postedBy, this.auth.user()?.id) : '',
    },
    {
      headerName: 'Status',
      valueGetter: (params) =>
        params.data?.reversedByDocumentId
          ? 'Reversed'
          : params.data?.reversesDocumentId
            ? 'Reverses another'
            : '',
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
