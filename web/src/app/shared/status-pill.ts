import { Component, input } from '@angular/core';
import type { StatusTone } from './format';

@Component({
  selector: 'app-status-pill',
  host: { class: 'pill', '[class]': '"pill--" + tone()', '[attr.data-tone]': 'tone()' },
  template: `<span class="dot" aria-hidden="true"></span>{{ label() }}`,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 2px 8px 2px 7px;
      border-radius: 999px;
      font: 500 12px/18px inherit;
      white-space: nowrap;
      background: var(--ims-neutral-bg);
      color: var(--ims-neutral-fg);
    }
    .dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--ims-neutral-dot);
    }
    :host(.pill--good) {
      background: var(--ims-good-bg);
      color: var(--ims-good-fg);
    }
    :host(.pill--good) .dot {
      background: var(--ims-good-dot);
    }
    :host(.pill--warn) {
      background: var(--ims-warn-bg);
      color: var(--ims-warn-fg);
    }
    :host(.pill--warn) .dot {
      background: var(--ims-warn-dot);
    }
    :host(.pill--bad) {
      background: var(--ims-bad-bg);
      color: var(--ims-bad-fg);
    }
    :host(.pill--bad) .dot {
      background: var(--ims-bad-dot);
    }
  `,
})
export class StatusPill {
  readonly tone = input<StatusTone>('neutral');
  readonly label = input.required<string>();
}
