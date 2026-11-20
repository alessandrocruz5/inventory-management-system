import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { PROBLEM_MESSAGES, problemMessage } from '../core/problems/problem-messages';
import { AssistantService, MAX_USER_CHARS } from './assistant.service';
import { DraftCard } from './draft-card';

const TOOL_LABELS: Record<string, string> = {
  get_stock: 'Checked stock',
  find_items: 'Searched items',
  get_item: 'Read an item',
  list_locations: 'Listed locations',
  get_item_history: 'Read item history',
  list_documents: 'Listed documents',
  get_document: 'Read a document',
  get_ledger_health: 'Checked ledger health',
  draft_document: 'Drafted a document',
};

const SUGGESTIONS = [
  'How much stock do we have at each location?',
  'What did we receive this week?',
  'Ilan pa ang bigas sa bawat branch?',
];

@Component({
  selector: 'app-assistant',
  imports: [DraftCard, MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './assistant.html',
  host: { class: 'page' },
  styleUrl: './assistant.scss',
})
export class Assistant {
  private readonly service = inject(AssistantService);

  protected readonly maxChars = MAX_USER_CHARS;
  protected readonly suggestions = SUGGESTIONS;
  protected readonly notConfiguredMessage = PROBLEM_MESSAGES['assistant-not-configured'];
  protected readonly toolLabel = (name: string) => TOOL_LABELS[name] ?? name;

  protected readonly conversation = this.service.conversation;
  protected readonly configured = signal<boolean | null>(null);
  protected readonly sending = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly draft = signal('');
  protected readonly canSend = computed(
    () => this.configured() === true && !this.sending() && this.draft().trim().length > 0,
  );

  constructor() {
    void this.loadStatus();
  }

  protected onInput(value: string): void {
    this.draft.set(value);
  }

  protected useSuggestion(text: string): void {
    this.draft.set(text);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      void this.send();
    }
  }

  protected async send(): Promise<void> {
    if (!this.canSend()) {
      return;
    }
    const text = this.draft();
    this.draft.set('');
    this.error.set(null);
    this.sending.set(true);
    try {
      await this.service.send(text);
    } catch (err) {
      this.draft.set(text);
      this.error.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
    } finally {
      this.sending.set(false);
    }
  }

  protected clear(): void {
    this.service.clear();
    this.error.set(null);
  }

  private async loadStatus(): Promise<void> {
    try {
      this.configured.set((await this.service.status()).configured);
    } catch (err) {
      this.configured.set(false);
      this.error.set(problemMessage(err instanceof HttpErrorResponse ? err.error : null));
    }
  }
}
