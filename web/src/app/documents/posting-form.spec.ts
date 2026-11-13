import { FormControl } from '@angular/forms';
import {
  IdempotencyKey,
  PostingHeader,
  PostingLine,
  amountValidator,
  buildPostingBody,
  duplicateItemLines,
  isOutflow,
  linesOverOnHand,
  postingErrors,
  sourceLocationId,
  unitCostRequired,
  usesUnitCost,
} from './posting-form';
import { PostingType } from './documents.service';

const header: PostingHeader = {
  locationId: 'loc-1',
  fromLocationId: 'loc-a',
  toLocationId: 'loc-b',
  reasonCode: 'Damaged',
  reference: '  PO-1  ',
  notes: '   ',
};

function line(over: Partial<PostingLine> = {}): PostingLine {
  return { itemId: 'item-1', quantity: 2, unitCost: 5.5, direction: 'add', ...over };
}

const TYPES: PostingType[] = ['receipt', 'issue', 'transfer', 'adjustment'];

describe('line editor', () => {
  it('builds a receipt body', () => {
    const body = buildPostingBody('receipt', header, [
      line(),
      line({ itemId: 'item-2', quantity: 1, unitCost: 0 }),
    ]);
    expect(body).toEqual({
      locationId: 'loc-1',
      reference: 'PO-1',
      notes: null,
      lines: [
        { itemId: 'item-1', quantity: 2, unitCost: 5.5 },
        { itemId: 'item-2', quantity: 1, unitCost: 0 },
      ],
    });
    expect(Object.keys(body)).toEqual(['locationId', 'reference', 'notes', 'lines']);
  });

  it('builds an issue body without unit cost', () => {
    expect(buildPostingBody('issue', { ...header, notes: ' hi ' }, [line()])).toEqual({
      locationId: 'loc-1',
      reference: 'PO-1',
      notes: 'hi',
      lines: [{ itemId: 'item-1', quantity: 2 }],
    });
  });

  it('builds a transfer body', () => {
    const body = buildPostingBody('transfer', { ...header, reference: '' }, [line()]);
    expect(body).toEqual({
      fromLocationId: 'loc-a',
      toLocationId: 'loc-b',
      reference: null,
      notes: null,
      lines: [{ itemId: 'item-1', quantity: 2 }],
    });
    expect(Object.keys(body)).toEqual([
      'fromLocationId',
      'toLocationId',
      'reference',
      'notes',
      'lines',
    ]);
  });

  it('builds an adjustment body with add and remove lines', () => {
    const body = buildPostingBody('adjustment', header, [
      line(),
      line({ itemId: 'item-2', quantity: 3, unitCost: 9, direction: 'remove' }),
    ]);
    expect(body).toEqual({
      locationId: 'loc-1',
      reasonCode: 'Damaged',
      reference: 'PO-1',
      notes: null,
      lines: [
        { itemId: 'item-1', quantityDelta: 2, unitCost: 5.5 },
        { itemId: 'item-2', quantityDelta: -3, unitCost: null },
      ],
    });
  });

  it('keeps a null cost on an adjustment add line', () => {
    const body = buildPostingBody('adjustment', header, [line({ unitCost: null })]);
    expect((body as { lines: unknown[] }).lines).toEqual([
      { itemId: 'item-1', quantityDelta: 2, unitCost: null },
    ]);
  });

  it('classifies unit cost, outflow and source location', () => {
    const add = { direction: 'add' as const };
    const remove = { direction: 'remove' as const };
    expect(TYPES.map((t) => usesUnitCost(t, add))).toEqual([true, false, false, true]);
    expect(TYPES.map((t) => usesUnitCost(t, remove))).toEqual([true, false, false, false]);
    expect(TYPES.map((t) => unitCostRequired(t))).toEqual([true, false, false, false]);
    expect(TYPES.map((t) => isOutflow(t, add))).toEqual([false, true, true, false]);
    expect(TYPES.map((t) => isOutflow(t, remove))).toEqual([false, true, true, true]);
    expect(TYPES.map((t) => sourceLocationId(t, header))).toEqual([
      'loc-1',
      'loc-1',
      'loc-a',
      'loc-1',
    ]);
  });

  it('flags only outflow lines over a known on-hand', () => {
    const lines = [
      line({ quantity: 5 }),
      line({ quantity: 3 }),
      line({ quantity: 9 }),
      line({ quantity: null }),
    ];
    expect(linesOverOnHand('issue', lines, [4, 3, null, 0])).toEqual([0]);
    expect(linesOverOnHand('receipt', lines, [0, 0, 0, 0])).toEqual([]);
    expect(
      linesOverOnHand(
        'adjustment',
        [line({ quantity: 5, direction: 'remove' }), line({ quantity: 5 })],
        [1, 1],
      ),
    ).toEqual([0]);
  });

  it('finds the second and later occurrences of an item', () => {
    const lines = [
      line({ itemId: 'a' }),
      line({ itemId: 'b' }),
      line({ itemId: 'a' }),
      line({ itemId: null }),
      line({ itemId: 'a' }),
    ];
    expect(duplicateItemLines(lines)).toEqual([2, 4]);
    expect(duplicateItemLines([])).toEqual([]);
  });

  describe('server problems', () => {
    const lines = [line({ itemId: 'a' }), line({ itemId: 'b' }), line({ itemId: 'a' })];

    it('maps validation keys to header and line fields', () => {
      const result = postingErrors(
        'adjustment',
        {
          type: '/problems/validation',
          errors: {
            reasonCode: ['Pick a reason.', 'second'],
            lines: ['Add a line.'],
            'lines[1].quantityDelta': ['Not zero.'],
            'lines[0].quantity': ['Too big.'],
            'lines[2].unitCost': ['Cost needed.'],
            'lines[2].itemId': ['Duplicate.'],
          },
        },
        lines,
      );
      expect(result.header).toEqual({ reasonCode: 'Pick a reason.', lines: 'Add a line.' });
      expect(result.lines).toEqual({
        0: { quantity: 'Too big.' },
        1: { quantity: 'Not zero.' },
        2: { unitCost: 'Cost needed.', itemId: 'Duplicate.' },
      });
      expect(result.message).toBeNull();
      expect(result.renewKey).toBe(false);
    });

    it('sets the message when a key maps nowhere', () => {
      const result = postingErrors(
        'issue',
        { type: '/problems/validation', errors: { mystery: ['??'], reference: ['Too long.'] } },
        lines,
      );
      expect(result.header.reference).toBe('Too long.');
      expect(result.message).toBe('Please check the highlighted fields.');
    });

    it('marks every line of the item on insufficient stock', () => {
      const result = postingErrors(
        'issue',
        {
          type: '/problems/insufficient-stock',
          itemId: 'a',
          locationId: 'l',
          available: 1.5,
          requested: 4,
        },
        lines,
        ['pcs', null, 'pcs'],
      );
      expect(result.lines).toEqual({
        0: { quantity: 'Only 1.5 pcs available; you asked for 4.' },
        2: { quantity: 'Only 1.5 pcs available; you asked for 4.' },
      });
      expect(result.message).toBe('There is not enough stock for one of the lines.');
      expect(result.renewKey).toBe(false);
    });

    it('omits the unit when none is given', () => {
      const result = postingErrors(
        'issue',
        { type: '/problems/insufficient-stock', itemId: 'b', available: 0, requested: 1000 },
        lines,
      );
      expect(result.lines[1].quantity).toBe('Only 0 available; you asked for 1,000.');
    });

    it('flags inactive items on their lines', () => {
      const result = postingErrors(
        'receipt',
        { type: '/problems/inactive-item', itemId: 'b' },
        lines,
      );
      expect(result.lines).toEqual({ 1: { itemId: 'This item is no longer active.' } });
      expect(result.message).toBe('One of the items is no longer active.');
    });

    it('shows a message for an inactive location', () => {
      const result = postingErrors(
        'receipt',
        { type: '/problems/inactive-location', locationId: 'x' },
        lines,
      );
      expect(result.message).toBe('This location is no longer active.');
      expect(result.lines).toEqual({});
      expect(result.renewKey).toBe(false);
    });

    it('reports a reused key and asks for a new one', () => {
      const result = postingErrors(
        'receipt',
        { type: '/problems/idempotency-key-reused', originalDocumentId: 'doc-9' },
        lines,
      );
      expect(result.originalDocumentId).toBe('doc-9');
      expect(result.renewKey).toBe(true);
      expect(result.message).toContain('already used');
    });

    it('keeps the same key for non-problem bodies', () => {
      for (const body of [{ status: 0 }, null, 'oops', { type: '/problems/unknown' }]) {
        const result = postingErrors('issue', body, lines);
        expect(result.renewKey).toBe(false);
        expect(result.originalDocumentId).toBeNull();
        expect(result.message).toBe('Something went wrong. Please try again.');
      }
    });
  });
});

describe('idempotency key', () => {
  it('is stable across reads and changes once on renew', () => {
    let n = 0;
    const key = new IdempotencyKey(() => `key-${++n}`);
    expect(key.current).toBe('key-1');
    expect(key.current).toBe('key-1');
    key.renew();
    expect(key.current).toBe('key-2');
    expect(key.current).toBe('key-2');
  });

  it('defaults to a random uuid', () => {
    const a = new IdempotencyKey();
    expect(a.current).toMatch(/^[0-9a-f-]{36}$/);
    expect(a.current).toBe(a.current);
  });
});

describe('amounts', () => {
  const positive = amountValidator({ allowZero: false });
  const nonNegative = amountValidator({ allowZero: true });
  const check = (fn: ReturnType<typeof amountValidator>, v: unknown) => fn(new FormControl(v));

  it('leaves empty values to required', () => {
    for (const v of [null, undefined, '']) {
      expect(check(positive, v)).toBeNull();
    }
  });

  it('rejects non-finite values', () => {
    for (const v of [NaN, Infinity, -Infinity, 'abc']) {
      expect(check(positive, v)).toEqual({ amount: 'Enter a number.' });
    }
  });

  it('enforces the minimum', () => {
    expect(check(positive, 0)).toEqual({ amount: 'Must be more than 0.' });
    expect(check(positive, -1)).toEqual({ amount: 'Must be more than 0.' });
    expect(check(nonNegative, 0)).toBeNull();
    expect(check(nonNegative, -0.5)).toEqual({ amount: 'Cannot be negative.' });
  });

  it('limits decimal places', () => {
    expect(check(positive, 0.1)).toBeNull();
    expect(check(positive, 0.1234)).toBeNull();
    expect(check(positive, 0.12345)).toEqual({ amount: 'Use at most 4 decimal places.' });
  });

  it('limits the range', () => {
    expect(check(positive, 999_999_999.9999)).toBeNull();
    expect(check(positive, 1_000_000_000)).toEqual({ amount: 'Must be at most 999,999,999.9999.' });
  });
});
