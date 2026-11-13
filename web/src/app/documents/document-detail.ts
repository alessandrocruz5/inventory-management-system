import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { problemMessage } from '../core/problems/problem-messages';
import {
  documentTypeLabel,
  formatManilaDateTime,
  formatMoney,
  formatQuantity,
  postedByLabel,
  reasonLabel,
} from '../shared/format';
import { DocumentDetail as DocumentDetailData, DocumentsService } from './documents.service';

@Component({
  selector: 'app-document-detail',
  imports: [RouterLink],
  templateUrl: './document-detail.html',
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
    dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 16px;
      margin: 0;
    }
    dt {
      color: var(--mat-sys-on-surface-variant);
    }
    dd {
      margin: 0;
    }
    table {
      border-collapse: collapse;
      width: 100%;
    }
    th,
    td {
      padding: 8px;
      text-align: left;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .num {
      text-align: right;
    }
    @media (max-width: 599px) {
      thead {
        display: none;
      }
      tr,
      td {
        display: block;
      }
      tr {
        padding: 8px 0;
        border-bottom: 1px solid var(--mat-sys-outline-variant);
      }
      td {
        border: 0;
        padding: 2px 0;
        text-align: left;
      }
      td[data-label]::before {
        content: attr(data-label) ': ';
        color: var(--mat-sys-on-surface-variant);
      }
    }
  `,
})
export class DocumentDetail {
  private readonly documentsService = inject(DocumentsService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  protected readonly typeLabel = documentTypeLabel;
  protected readonly reasonLabel = reasonLabel;
  protected readonly formatDate = formatManilaDateTime;
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;

  protected readonly document = signal<DocumentDetailData | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  private requestedId: string | null = null;

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((params) => {
      const id = params.get('id');
      if (id) {
        void this.load(id);
      }
    });
  }

  protected postedBy(doc: DocumentDetailData): string {
    return postedByLabel(doc.postedBy, this.auth.user()?.id);
  }

  private async load(id: string): Promise<void> {
    this.requestedId = id;
    this.loading.set(true);
    this.error.set(null);
    this.document.set(null);
    try {
      const doc = await this.documentsService.get(id);
      if (this.requestedId === id) {
        this.document.set(doc);
      }
    } catch (err) {
      if (this.requestedId === id) {
        this.error.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
      }
    } finally {
      if (this.requestedId === id) {
        this.loading.set(false);
      }
    }
  }
}
