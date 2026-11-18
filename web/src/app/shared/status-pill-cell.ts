import { Component, signal } from '@angular/core';
import type { ICellRendererAngularComp } from 'ag-grid-angular';
import type { ICellRendererParams } from 'ag-grid-community';
import type { Status } from './format';
import { StatusPill } from './status-pill';

/** AG Grid cell renderer: the column's valueGetter supplies a `Status`; pinned rows supply nothing. */
@Component({
  selector: 'app-status-pill-cell',
  imports: [StatusPill],
  template: `@if (status(); as s) {
    <app-status-pill [tone]="s.tone" [label]="s.label" />
  }`,
})
export class StatusPillCell implements ICellRendererAngularComp {
  protected readonly status = signal<Status | null>(null);

  agInit(params: ICellRendererParams<unknown, Status | null | undefined>): void {
    this.status.set(params.value ?? null);
  }

  refresh(params: ICellRendererParams<unknown, Status | null | undefined>): boolean {
    this.status.set(params.value ?? null);
    return true;
  }
}
