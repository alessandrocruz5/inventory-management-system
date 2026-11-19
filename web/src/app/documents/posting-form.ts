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
  /** The entered unit; null means the item's base unit (and then no `unit` is sent). */
  unit: string | null;
  /** Base units per entered unit; 1 for the base unit. */
  factor: number;
}

export const POSTING_TITLES: Record<PostingType, string> = {
  receipt: 'Receive stock',
  issue: 'Use or sell stock',
  transfer: 'Move stock',
  adjustment: 'Correct stock',
};

export const POSTING_SUBTITLES: Record<PostingType, string> = {
  receipt: 'Record stock arriving from a supplier.',
  issue: 'Record stock that was sold or used up.',
  transfer: 'Move stock between two of your locations.',
  adjustment: 'Fix a count, or write off damaged or lost stock.',
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

/** Rounds to 4 dp, AwayFromZero (the server's rounding). */
function round4(value: number): number {
  return (Math.sign(value) * Math.round(Math.abs(value) * 10000)) / 10000;
}

/** The line's quantity in the item's base unit; null while the quantity is empty or not a number. */
export function baseQuantity(line: Pick<PostingLine, 'quantity' | 'factor'>): number | null {
  return line.quantity !== null && Number.isFinite(line.quantity)
    ? round4(line.quantity * line.factor)
    : null;
}

/** `{ unit }` for a line entered in a non-base unit, else nothing (a base line's body is unchanged). */
function unitPart(line: Pick<PostingLine, 'unit'>): { unit?: string } {
  return line.unit !== null ? { unit: line.unit } : {};
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
          ...unitPart(l),
        })),
      } satisfies ReceiptBody;
    case 'issue':
      return {
        locationId: header.locationId!,
        reference,
        notes,
        lines: lines.map((l) => ({ itemId: l.itemId!, quantity: l.quantity!, ...unitPart(l) })),
      } satisfies IssueBody;
    case 'transfer':
      return {
        fromLocationId: header.fromLocationId!,
        toLocationId: header.toLocationId!,
        reference,
        notes,
        lines: lines.map((l) => ({ itemId: l.itemId!, quantity: l.quantity!, ...unitPart(l) })),
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
          ...unitPart(l),
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
    const want = baseQuantity(line);
    if (isOutflow(type, line) && want != null && have != null && want > have) {
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
  lines: Record<number, Partial<Record<'itemId' | 'quantity' | 'unitCost' | 'unit', string>>>;
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

const LINE_KEY = /^lines\[(\d+)\]\.(itemId|quantity|quantityDelta|unitCost|unit)$/;

function asRecord(body: unknown): Record<string, unknown> {
  return typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : {};
}

/** `baseUnits[i]` is line i's item base unit: `insufficient-stock` reports `available`/`requested` in base units. */
export function postingErrors(
  type: PostingType,
  body: unknown,
  lines: readonly PostingLine[],
  baseUnits: readonly (string | null)[] = [],
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
  const setLine = (
    i: number,
    field: 'itemId' | 'quantity' | 'unitCost' | 'unit',
    text: string,
  ): void => {
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
              : (line[2] as 'itemId' | 'quantity' | 'unitCost' | 'unit');
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
          const unit = baseUnits[i] ? ` ${baseUnits[i]}` : '';
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

/** "Total cost" for types with a cost field (receipt, adjustment); else "Value at average cost". */
export function postingTotalLabel(type: PostingType): 'Total cost' | 'Value at average cost' {
  return type === 'receipt' || type === 'adjustment' ? 'Total cost' : 'Value at average cost';
}

/**
 * The sticky-bar total (display only; the server costs the posting). Per line:
 * usesUnitCost -> quantity x (unitCost ?? 0), the cost being per entered unit; otherwise
 * quantity x factor x (averageCosts[i] ?? 0), the average cost being per base unit.
 * Lines with a null or non-finite quantity count as 0. Rounded to 4 dp, AwayFromZero.
 */
export function postingTotal(
  type: PostingType,
  lines: readonly PostingLine[],
  averageCosts: readonly (number | null)[],
): number {
  let total = 0;
  lines.forEach((line, i) => {
    if (usesUnitCost(type, line)) {
      const quantity = line.quantity !== null && Number.isFinite(line.quantity) ? line.quantity : 0;
      total += quantity * (line.unitCost ?? 0);
    } else {
      total += (baseQuantity(line) ?? 0) * (averageCosts[i] ?? 0);
    }
  });
  return round4(total);
}
