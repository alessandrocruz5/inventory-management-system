import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { PROBLEM_MESSAGES, problemMessage, problemSlug } from '../core/problems/problem-messages';
import { ConfirmDialog, ConfirmDialogData } from '../shared/confirm-dialog';
import { StatusPill } from '../shared/status-pill';
import {
  documentStatus,
  documentTypeLabel,
  formatManilaDateTime,
  formatMoney,
  formatQuantity,
  postedByLabel,
  reasonLabel,
} from '../shared/format';
import { DocumentDetail as DocumentDetailData, DocumentsService } from './documents.service';
import { IdempotencyKey } from './posting-form';

@Component({
  selector: 'app-document-detail',
  imports: [RouterLink, MatButtonModule, MatIconModule, StatusPill],
  templateUrl: './document-detail.html',
  styleUrl: './document-detail.scss',
  host: { class: 'page' },
})
export class DocumentDetail {
  private readonly documentsService = inject(DocumentsService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly dialog = inject(MatDialog);

  protected readonly typeLabel = documentTypeLabel;
  protected readonly reasonLabel = reasonLabel;
  protected readonly formatDate = formatManilaDateTime;
  protected readonly formatMoney = formatMoney;
  protected readonly formatQuantity = formatQuantity;
  protected readonly status = documentStatus;

  protected readonly document = signal<DocumentDetailData | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  protected readonly reversing = signal(false);
  protected readonly reverseError = signal<{
    message: string;
    linkId: string | null;
    linkLabel: string;
  } | null>(null);
  protected readonly canReverse = computed(() => {
    const d = this.document();
    return (
      this.auth.isAdmin() && d !== null && d.type !== 'Reversal' && d.reversedByDocumentId === null
    );
  });

  protected readonly linesTotal = computed(() => {
    const total = (this.document()?.lines ?? []).reduce((sum, l) => sum + l.totalCost, 0);
    return Math.round(total * 10000) / 10000;
  });

  private readonly reverseKey = new IdempotencyKey();
  private requestedId: string | null = null;
  private loadedId: string | null = null;

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

  protected async reverse(): Promise<void> {
    const doc = this.document();
    if (!doc || this.reversing()) {
      return;
    }
    const confirmed = await new Promise<boolean>((resolve) => {
      this.dialog
        .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
          data: {
            title: 'Reverse document',
            message: `Reverse this ${documentTypeLabel(doc.type).toLowerCase()} document? A reversal document undoes its stock movements. It can't be edited later.`,
            confirm: 'Reverse',
          },
        })
        .afterClosed()
        .subscribe((result) => resolve(result === true));
    });
    if (!confirmed) {
      return;
    }
    this.reversing.set(true);
    this.reverseError.set(null);
    try {
      await this.documentsService.reverse(doc.id, this.reverseKey.current);
      this.reverseKey.renew();
      await this.load(doc.id);
    } catch (err) {
      const body = err instanceof HttpErrorResponse ? err.error : null;
      switch (problemSlug(body?.type)) {
        case 'reversal-not-latest':
          this.reverseError.set({
            message: PROBLEM_MESSAGES['reversal-not-latest'],
            linkId: body.blockingDocumentId ?? null,
            linkLabel: 'Open the later document',
          });
          break;
        case 'already-reversed':
          this.reverseError.set({
            message: PROBLEM_MESSAGES['already-reversed'],
            linkId: body.reversalDocumentId ?? null,
            linkLabel: 'Open the reversal',
          });
          await this.load(doc.id);
          break;
        case 'idempotency-key-reused':
          this.reverseKey.renew();
          this.reverseError.set({ message: problemMessage(body), linkId: null, linkLabel: '' });
          break;
        default:
          this.reverseError.set({ message: problemMessage(body), linkId: null, linkLabel: '' });
      }
    } finally {
      this.reversing.set(false);
    }
  }

  private async load(id: string): Promise<void> {
    if (this.loadedId !== id) {
      this.loadedId = id;
      this.reverseKey.renew();
      this.reverseError.set(null);
    }
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
