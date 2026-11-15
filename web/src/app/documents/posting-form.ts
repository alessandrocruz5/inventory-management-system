import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import { problemFieldErrors, problemMessage, problemSlug } from '../core/problems/problem-messages';
import { AdjustmentReason, formatQuantity } from '../shared/format';
import {
  AdjustmentBody,
  IssueBody,
  PostingBody,
  PostingType,
  ReceiptBody,
  TransferBody,
} from './documents.service';

export interface PostingHeader {
  locationId: string | null;
  fromLocationId: string | null;
  toLocationId: string | null;
  reasonCode: AdjustmentReason | null;
  reference: string;
  notes: string;
}

export type LineDirection = 'add' | 'remove';

export interface PostingLine {
  itemId: string | null;
  quantity: number | null;
  unitCost: number | null;
  direction: LineDirection;
}

export const POSTING_TITLES: Record<PostingType, string> = {
  receipt: 'Receive stock',
  issue: 'Use or sell stock',
  transfer: 'Move stock',
  adjustment: 'Correct stock',
};

export const MAX_LINES = 100;

// Mirrors PostingValidation.MaxAmount on the server
// (src/Modules/Inventory/Inventory/Features/Posting/PostingValidation.cs:40).
// 13 significant digits keeps every 4-dp value exact in a double.
const MAX_AMOUNT = 999_999_999.9999;

export function usesUnitCost(type: PostingType, line: Pick<PostingLine, 'direction'>): boolean {
  return type === 'receipt' || (type === 'adjustment' && line.direction === 'add');
}

export function unitCostRequired(type: PostingType): boolean {
  return type === 'receipt';
}

export function isOutflow(type: PostingType, line: Pick<PostingLine, 'direction'>): boolean {
  return (
    type === 'issue' ||
    type === 'transfer' ||
    (type === 'adjustment' && line.direction === 'remove')
  );
}

export function sourceLocationId(type: PostingType, header: PostingHeader): string | null {
  return type === 'transfer' ? header.fromLocationId : header.locationId;
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

export function buildPostingBody(
  type: PostingType,
  header: PostingHeader,
  lines: readonly PostingLine[],
): PostingBody {
  const reference = blankToNull(header.reference);
  const notes = blankToNull(header.notes);
  switch (type) {
    case 'receipt':
      return {
        locationId: header.locationId!,
        reference,
        notes,
        lines: lines.map((l) => ({
          itemId: l.itemId!,
          quantity: l.quantity!,
          unitCost: l.unitCost!,
        })),
      } satisfies ReceiptBody;
    case 'issue':
      return {
        locationId: header.locationId!,
        reference,
        notes,
        lines: lines.map((l) => ({ itemId: l.itemId!, quantity: l.quantity! })),
      } satisfies IssueBody;
    case 'transfer':
      return {
        fromLocationId: header.fromLocationId!,
        toLocationId: header.toLocationId!,
        reference,
        notes,
        lines: lines.map((l) => ({ itemId: l.itemId!, quantity: l.quantity! })),
      } satisfies TransferBody;
    case 'adjustment':
      return {
        locationId: header.locationId!,
        reasonCode: header.reasonCode!,
        reference,
        notes,
        lines: lines.map((l) => ({
          itemId: l.itemId!,
          quantityDelta: l.direction === 'add' ? l.quantity! : -l.quantity!,
          unitCost: l.direction === 'add' ? l.unitCost : null,
        })),
      } satisfies AdjustmentBody;
  }
}

export function linesOverOnHand(
  type: PostingType,
  lines: readonly PostingLine[],
  onHand: readonly (number | null)[],
): number[] {
  const result: number[] = [];
  lines.forEach((line, i) => {
    const have = onHand[i];
    if (isOutflow(type, line) && line.quantity != null && have != null && line.quantity > have) {
      result.push(i);
    }
  });
  return result;
}

export function duplicateItemLines(lines: readonly PostingLine[]): number[] {
  const seen = new Set<string>();
  const result: number[] = [];
  lines.forEach((line, i) => {
    if (line.itemId == null) {
      return;
    }
    if (seen.has(line.itemId)) {
      result.push(i);
    } else {
      seen.add(line.itemId);
    }
  });
  return result;
}

export function amountValidator(options: { allowZero: boolean }): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value: unknown = control.value;
    if (value === null || value === undefined || value === '') {
      return null;
    }
    if (typeof value !== 'number' || !Number.isFinite(value)) {
      return { amount: 'Enter a number.' };
    }
    if (options.allowZero ? value < 0 : value <= 0) {
      return { amount: options.allowZero ? 'Cannot be negative.' : 'Must be more than 0.' };
    }
    if (Number(value.toFixed(4)) !== value) {
      return { amount: 'Use at most 4 decimal places.' };
    }
    if (Math.abs(value) > MAX_AMOUNT) {
      return { amount: 'Must be at most 999,999,999.9999.' };
    }
    return null;
  };
}

export class IdempotencyKey {
  private readonly generate: () => string;
  private value: string;

  constructor(generate: () => string = () => crypto.randomUUID()) {
    this.generate = generate;
    this.value = generate();
  }

  get current(): string {
    return this.value;
  }

  renew(): void {
    this.value = this.generate();
  }
}

export interface PostingErrors {
  header: Partial<Record<keyof PostingHeader | 'lines', string>>;
  lines: Record<number, Partial<Record<'itemId' | 'quantity' | 'unitCost', string>>>;
  message: string | null;
  originalDocumentId: string | null;
  renewKey: boolean;
}

const HEADER_KEYS: readonly string[] = [
  'locationId',
  'fromLocationId',
  'toLocationId',
  'reasonCode',
  'reference',
  'notes',
];

const LINE_KEY = /^lines\[(\d+)\]\.(itemId|quantity|quantityDelta|unitCost)$/;

function asRecord(body: unknown): Record<string, unknown> {
  return typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : {};
}

export function postingErrors(
  type: PostingType,
  body: unknown,
  lines: readonly PostingLine[],
  units: readonly (string | null)[] = [],
): PostingErrors {
  void type;
  const result: PostingErrors = {
    header: {},
    lines: {},
    message: null,
    originalDocumentId: null,
    renewKey: false,
  };
  const fields = asRecord(body);
  const setLine = (i: number, field: 'itemId' | 'quantity' | 'unitCost', text: string): void => {
    (result.lines[i] ??= {})[field] ??= text;
  };

  switch (problemSlug(fields['type'])) {
    case 'validation': {
      let unmapped = false;
      for (const [key, messages] of Object.entries(problemFieldErrors(body))) {
        const text = messages[0];
        if (text === undefined) {
          unmapped = true;
          continue;
        }
        const line = LINE_KEY.exec(key);
        if (HEADER_KEYS.includes(key)) {
          result.header[key as keyof PostingHeader] = text;
        } else if (key === 'lines' || /^lines\[\d+\]$/.test(key)) {
          result.header.lines = text;
        } else if (line) {
          const field =
            line[2] === 'quantityDelta'
              ? 'quantity'
              : (line[2] as 'itemId' | 'quantity' | 'unitCost');
          setLine(Number(line[1]), field, text);
        } else {
          unmapped = true;
        }
      }
      result.message = unmapped ? problemMessage(body) : null;
      break;
    }
    case 'insufficient-stock': {
      const available = fields['available'];
      const requested = fields['requested'];
      lines.forEach((line, i) => {
        if (line.itemId !== null && line.itemId === fields['itemId']) {
          const unit = units[i] ? ` ${units[i]}` : '';
          const have =
            typeof available === 'number' ? formatQuantity(available) : String(available);
          const want =
            typeof requested === 'number' ? formatQuantity(requested) : String(requested);
          setLine(i, 'quantity', `Only ${have}${unit} available; you asked for ${want}.`);
        }
      });
      result.message = problemMessage(body);
      break;
    }
    case 'inactive-item':
      lines.forEach((line, i) => {
        if (line.itemId !== null && line.itemId === fields['itemId']) {
          setLine(i, 'itemId', 'This item is no longer active.');
        }
      });
      result.message = problemMessage(body);
      break;
    case 'idempotency-key-reused':
      result.originalDocumentId =
        typeof fields['originalDocumentId'] === 'string' ? fields['originalDocumentId'] : null;
      result.renewKey = true;
      result.message = problemMessage(body);
      break;
    default:
      result.message = problemMessage(body);
  }
  return result;
}
