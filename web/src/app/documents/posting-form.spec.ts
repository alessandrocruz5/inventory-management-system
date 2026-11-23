import { FormControl } from '@angular/forms';
import {
  IdempotencyKey,
  POSTING_SUBTITLES,
  PostingHeader,
  PostingLine,
  amountValidator,
  buildPostingBody,
  draftToPrefill,
  duplicateItemLines,
  isOutflow,
  linesOverOnHand,
  postingErrors,
  postingTotal,
  postingTotalLabel,
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
  return {
    itemId: 'item-1',
    quantity: 2,
    unitCost: 5.5,
    direction: 'add',
    unit: null,
    factor: 1,
    ...over,
  };
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

  it('leaves the unit out of a base line and sends it for a case line', () => {
    const body = buildPostingBody('receipt', header, [
      line(),
      line({ itemId: 'item-2', quantity: 1, unitCost: 1250, unit: 'case', factor: 25 }),
    ]);
    expect(body).toEqual({
      locationId: 'loc-1',
      reference: 'PO-1',
      notes: null,
      lines: [
        { itemId: 'item-1', quantity: 2, unitCost: 5.5 },
        { itemId: 'item-2', quantity: 1, unitCost: 1250, unit: 'case' },
      ],
    });
    const base = (body as { lines: object[] }).lines[0];
    expect(Object.keys(base)).toEqual(['itemId', 'quantity', 'unitCost']);
    const issue = buildPostingBody('issue', header, [line({ unit: 'case', factor: 25 })]);
    expect((issue as { lines: object[] }).lines).toEqual([
      { itemId: 'item-1', quantity: 2, unit: 'case' },
    ]);
    const adjust = buildPostingBody('adjustment', header, [
      line({ direction: 'remove', unit: 'case', factor: 25 }),
    ]);
    expect((adjust as { lines: object[] }).lines).toEqual([
      { itemId: 'item-1', quantityDelta: -2, unitCost: null, unit: 'case' },
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

  it('compares the quantity in base units with on-hand', () => {
    // 2 cases of 25 = 50 base units.
    const lines = [line({ quantity: 2, unit: 'case', factor: 25 })];
    expect(linesOverOnHand('issue', lines, [49])).toEqual([0]);
    expect(linesOverOnHand('issue', lines, [50])).toEqual([]);
    expect(
      linesOverOnHand('issue', [line({ quantity: 0.3, unit: 'x', factor: 0.1 })], [0.03]),
    ).toEqual([]);
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

    it('maps lines[i].unit onto the unit field', () => {
      const result = postingErrors(
        'receipt',
        {
          type: '/problems/validation',
          errors: { 'lines[0].unit': ['Unknown unit.'], 'lines[1].quantity': ['Not whole.'] },
        },
        lines,
      );
      expect(result.lines).toEqual({
        0: { unit: 'Unknown unit.' },
        1: { quantity: 'Not whole.' },
      });
      expect(result.message).toBeNull();
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

describe('postingTotal', () => {
  it('sums quantity x unit cost for a receipt', () => {
    const lines = [line({ quantity: 10, unitCost: 85.5 }), line({ quantity: 2, unitCost: 142 })];
    expect(postingTotal('receipt', lines, [null, null])).toBe(1139);
  });

  it('uses average costs for an issue', () => {
    const lines = [line({ quantity: 3 }), line({ quantity: 2 })];
    expect(postingTotal('issue', lines, [85.5, null])).toBe(256.5);
  });

  it('counts adjustment Add lines at unit cost and Remove lines at average cost', () => {
    const lines = [
      line({ quantity: 2, unitCost: 10, direction: 'add' }),
      line({ quantity: 1, unitCost: 99, direction: 'remove' }),
    ];
    expect(postingTotal('adjustment', lines, [null, 7])).toBe(27);
  });

  it('ignores a null quantity', () => {
    const lines = [line({ quantity: null, unitCost: 50 }), line({ quantity: 2, unitCost: 5 })];
    expect(postingTotal('receipt', lines, [null, null])).toBe(10);
  });

  it('values an issue in base units at the average cost', () => {
    // 2 cases of 25 base units at 4.5 per base unit.
    const lines = [line({ quantity: 2, unit: 'case', factor: 25 })];
    expect(postingTotal('issue', lines, [4.5])).toBe(225);
  });

  it('keeps a unit-cost line at quantity x unit cost in the entered unit', () => {
    const lines = [line({ quantity: 1, unitCost: 1250, unit: 'sack', factor: 25 })];
    expect(postingTotal('receipt', lines, [null])).toBe(1250);
  });

  it('rounds to 4 decimal places', () => {
    expect(postingTotal('receipt', [line({ quantity: 3, unitCost: 0.1 })], [null])).toBe(0.3);
  });
});

describe('postingTotalLabel', () => {
  it('labels each posting type', () => {
    expect(postingTotalLabel('receipt')).toBe('Total cost');
    expect(postingTotalLabel('adjustment')).toBe('Total cost');
    expect(postingTotalLabel('issue')).toBe('Value at average cost');
    expect(postingTotalLabel('transfer')).toBe('Value at average cost');
  });
});

describe('POSTING_SUBTITLES', () => {
  it('has a subtitle for every posting type', () => {
    const types: PostingType[] = ['receipt', 'issue', 'transfer', 'adjustment'];
    for (const type of types) {
      expect(POSTING_SUBTITLES[type].length).toBeGreaterThan(0);
    }
  });
});

describe('draftToPrefill', () => {
  const loc = (id: string) => ({ id, code: id.toUpperCase(), name: id });
  const line = (extra: Record<string, unknown> = {}) => ({
    itemId: 'i1',
    sku: 'SKU-1',
    itemName: 'Widget',
    baseUnit: 'kg',
    quantity: 2,
    unit: null,
    unitCost: null,
    direction: null,
    ...extra,
  });
  const draft = (type: string, extra: Record<string, unknown> = {}) => ({
    type,
    location: loc('l1'),
    reasonCode: null,
    reference: null,
    notes: null,
    lines: [line()],
    warnings: [],
    ...extra,
  });

  it('maps a receipt with a unit and a unit cost', () => {
    const d = draft('receipt', {
      reference: 'PO-1',
      lines: [line({ unit: 'sack', unitCost: 3650 })],
    });
    expect(draftToPrefill('receipt', d)).toEqual({
      header: {
        locationId: 'l1',
        fromLocationId: null,
        toLocationId: null,
        reasonCode: null,
        reference: 'PO-1',
        notes: '',
      },
      lines: [{ itemId: 'i1', quantity: 2, unitCost: 3650, unit: 'sack', direction: 'add' }],
    });
  });

  it('maps an issue', () => {
    const p = draftToPrefill('issue', draft('issue', { notes: 'for the bakery' }));
    expect(p?.header.locationId).toBe('l1');
    expect(p?.header.notes).toBe('for the bakery');
    expect(p?.lines[0]).toEqual({
      itemId: 'i1',
      quantity: 2,
      unitCost: null,
      unit: null,
      direction: 'add',
    });
  });

  it('maps a transfer', () => {
    const p = draftToPrefill(
      'transfer',
      draft('transfer', { location: undefined, fromLocation: loc('l1'), toLocation: loc('l2') }),
    );
    expect(p?.header.fromLocationId).toBe('l1');
    expect(p?.header.toLocationId).toBe('l2');
    expect(p?.header.locationId).toBeNull();
  });

  it('maps an adjustment with its direction and reason', () => {
    const p = draftToPrefill(
      'adjustment',
      draft('adjustment', { reasonCode: 'Damaged', lines: [line({ direction: 'remove' })] }),
    );
    expect(p?.header.reasonCode).toBe('Damaged');
    expect(p?.lines[0].direction).toBe('remove');
  });

  it('ignores an unknown reason code', () => {
    const p = draftToPrefill(
      'adjustment',
      draft('adjustment', { reasonCode: 'Stolen', lines: [line({ direction: 'add' })] }),
    );
    expect(p?.header.reasonCode).toBeNull();
  });

  it('returns null when the draft is not usable', () => {
    const hundredOne = Array.from({ length: 101 }, () => line());
    expect(draftToPrefill('issue', draft('receipt'))).toBeNull();
    expect(draftToPrefill('receipt', draft('receipt', { lines: [] }))).toBeNull();
    expect(draftToPrefill('receipt', draft('receipt', { lines: hundredOne }))).toBeNull();
    expect(
      draftToPrefill('receipt', draft('receipt', { lines: [line({ quantity: -1 })] })),
    ).toBeNull();
    expect(
      draftToPrefill('receipt', draft('receipt', { lines: [line({ quantity: 0 })] })),
    ).toBeNull();
    expect(
      draftToPrefill('receipt', draft('receipt', { lines: [line({ unitCost: -1 })] })),
    ).toBeNull();
    expect(
      draftToPrefill('receipt', draft('receipt', { lines: [line({ itemId: undefined })] })),
    ).toBeNull();
    expect(
      draftToPrefill('receipt', draft('receipt', { lines: [line({ quantity: Infinity })] })),
    ).toBeNull();
    expect(draftToPrefill('receipt', draft('receipt', { location: null }))).toBeNull();
    expect(
      draftToPrefill('adjustment', draft('adjustment', { lines: [line({ direction: null })] })),
    ).toBeNull();
    expect(draftToPrefill('receipt', null)).toBeNull();
    expect(draftToPrefill('receipt', 'receipt')).toBeNull();
    expect(draftToPrefill('receipt', [])).toBeNull();
    expect(draftToPrefill('receipt', undefined)).toBeNull();
  });
});
