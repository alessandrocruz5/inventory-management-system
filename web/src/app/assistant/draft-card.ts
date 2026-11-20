import { Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from '../core/auth/auth.service';
import { POSTING_TITLES } from '../documents/posting-form';
import { formatMoney, formatQuantity, reasonLabel } from '../shared/format';
import { StatusPill } from '../shared/status-pill';
import { AssistantDraft } from './assistant.service';

@Component({
  selector: 'app-draft-card',
  imports: [MatButtonModule, StatusPill],
  template: `
    <section class="draft" aria-label="Draft document">
      <header class="head">
        <strong>{{ title() }}</strong>
        <app-status-pill tone="neutral" label="Draft" />
      </header>
      <p class="where">{{ where() }}</p>
      @if (draft().reasonCode; as reason) {
        <p class="meta">Reason: {{ reasonText(reason) }}</p>
      }
      @if (draft().reference; as ref) {
        <p class="meta">Reference: {{ ref }}</p>
      }
      <ul class="lines">
        @for (line of draft().lines; track $index) {
          <li>
            <span class="name">{{ line.sku }} · {{ line.itemName }}</span>
            <span class="qty">
              @if (line.direction) {
                {{ line.direction === 'add' ? 'Add' : 'Remove' }}
              }
              {{ qty(line.quantity) }} {{ line.unit ?? line.baseUnit }}
              @if (line.unitCost !== null && line.unitCost !== undefined) {
                @ {{ money(line.unitCost) }}
              }
            </span>
          </li>
        }
      </ul>
      @if (draft().warnings.length > 0) {
        <ul class="warnings">
          @for (w of draft().warnings; track $index) {
            <li>{{ w }}</li>
          }
        </ul>
      }
      <footer class="foot">
        @if (canReview()) {
          <button mat-flat-button type="button" (click)="review()">Review and post</button>
        }
        <span class="muted">Not posted yet.</span>
      </footer>
    </section>
  `,
  styles: `
    :host {
      display: block;
    }
    .draft {
      margin-top: 8px;
      padding: 12px 16px;
      border: 1px solid var(--ims-border, #d8d8d8);
      border-radius: 8px;
    }
    .head {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .where,
    .meta {
      margin: 4px 0 0;
    }
    .lines,
    .warnings {
      margin: 8px 0 0;
      padding: 0;
      list-style: none;
    }
    .lines li {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: 4px 12px;
      padding: 4px 0;
    }
    .warnings {
      padding: 8px 12px;
      border-radius: 6px;
      background: var(--ims-warn-bg);
      color: var(--ims-warn-fg);
    }
    .foot {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 12px;
      margin-top: 12px;
    }
    .muted {
      opacity: 0.7;
    }
  `,
})
export class DraftCard {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);

  readonly draft = input.required<AssistantDraft>();

  protected readonly qty = formatQuantity;
  protected readonly money = formatMoney;
  protected readonly reasonText = reasonLabel;
  protected readonly title = computed(() => POSTING_TITLES[this.draft().type]);
  protected readonly where = computed(() => {
    const d = this.draft();
    const label = (l: { code: string; name: string } | null | undefined) =>
      l ? `${l.code} · ${l.name}` : '';
    if (d.type === 'transfer') {
      return `From ${d.fromLocation?.code ?? '?'} to ${d.toLocation?.code ?? '?'}`;
    }
    return `At ${label(d.location)}`;
  });
  protected readonly canReview = computed(
    () => !(this.draft().type === 'adjustment' && !this.auth.isAdmin()),
  );

  protected review(): void {
    const draft = this.draft();
    void this.router.navigate(['/documents/new', draft.type], { state: { assistantDraft: draft } });
  }
}
