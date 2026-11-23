import { HttpClient } from '@angular/common/http';
import { Injectable, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { PostingType } from '../documents/documents.service';
import { AdjustmentReason } from '../shared/format';

export const MAX_MESSAGES = 20;
export const MAX_USER_CHARS = 2000;
export const MAX_TOTAL_CHARS = 24000;

export interface AssistantStatus {
  configured: boolean;
  model: string | null;
}

export interface ToolCallSummary {
  name: string;
  arguments: Record<string, unknown> | null;
  succeeded: boolean;
}

export interface AssistantDraftLocation {
  id: string;
  code: string;
  name: string;
}

export interface AssistantDraftLine {
  itemId: string;
  sku: string;
  itemName: string;
  baseUnit: string;
  quantity: number;
  unit: string | null;
  unitCost: number | null;
  direction: 'add' | 'remove' | null;
}

export interface AssistantDraft {
  type: PostingType;
  location?: AssistantDraftLocation | null;
  fromLocation?: AssistantDraftLocation | null;
  toLocation?: AssistantDraftLocation | null;
  reasonCode?: AdjustmentReason | null;
  reference?: string | null;
  notes?: string | null;
  lines: AssistantDraftLine[];
  warnings: string[];
}

export interface AssistantReply {
  reply: string;
  toolCalls: ToolCallSummary[];
  drafts: AssistantDraft[];
  usage: { inputTokens: number; outputTokens: number; modelCalls: number };
  model: string;
}

export interface ChatEntry {
  role: 'user' | 'assistant';
  content: string;
  toolCalls?: ToolCallSummary[];
  drafts?: AssistantDraft[];
}

@Injectable({ providedIn: 'root' })
export class AssistantService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  private readonly _conversation = signal<ChatEntry[]>([]);
  readonly conversation = this._conversation.asReadonly();

  constructor() {
    let lastUserId: string | undefined;
    effect(() => {
      const id = this.auth.user()?.id;
      if (id !== lastUserId) {
        lastUserId = id;
        this._conversation.set([]);
      }
    });
  }

  status(): Promise<AssistantStatus> {
    return firstValueFrom(this.http.get<AssistantStatus>('/api/assistant/status'));
  }

  async send(text: string): Promise<void> {
    const content = text.trim();
    if (content.length === 0 || content.length > MAX_USER_CHARS) {
      throw new Error('Message must be 1 to 2,000 characters.');
    }
    const userEntry: ChatEntry = { role: 'user', content };
    this._conversation.update((c) => [...c, userEntry]);

    const history = this.buildHistory(this._conversation());
    try {
      const reply = await firstValueFrom(
        this.http.post<AssistantReply>('/api/assistant/chat', { messages: history }),
      );
      this._conversation.update((c) => [
        ...c,
        {
          role: 'assistant',
          content: reply.reply,
          toolCalls: reply.toolCalls,
          drafts: reply.drafts,
        },
      ]);
    } catch (error) {
      this._conversation.update((c) => c.filter((e) => e !== userEntry));
      throw error;
    }
  }

  clear(): void {
    this._conversation.set([]);
  }

  private buildHistory(entries: ChatEntry[]): { role: string; content: string }[] {
    const history = entries.slice(-MAX_MESSAGES).map((e) => ({ role: e.role, content: e.content }));
    let total = history.reduce((sum, m) => sum + m.content.length, 0);
    while (history.length > 1 && total > MAX_TOTAL_CHARS) {
      total -= history.shift()!.content.length;
    }
    return history;
  }
}
