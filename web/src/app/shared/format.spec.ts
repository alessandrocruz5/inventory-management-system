import {
  ADJUSTMENT_REASONS,
  SYSTEM_USER_ID,
  documentStatus,
  documentTypeLabel,
  formatManilaDateTime,
  formatMoney,
  formatQuantity,
  manilaDayRange,
  postedByLabel,
  reasonLabel,
  stockStatus,
} from './format';

describe('format', () => {
  it('formatMoney', () => {
    expect(formatMoney(1234.5)).toBe('₱1,234.50');
    expect(formatMoney(0)).toBe('₱0.00');
    expect(formatMoney(1234.505)).toBe('₱1,234.51');
    expect(formatMoney(2.345)).toBe('₱2.35');
    expect(formatMoney(-5)).toBe('-₱5.00');
  });

  it('formatQuantity trims trailing zeros', () => {
    expect(formatQuantity(10)).toBe('10');
    expect(formatQuantity(1.25)).toBe('1.25');
    expect(formatQuantity(1234.5)).toBe('1,234.5');
    expect(formatQuantity(0.0001)).toBe('0.0001');
  });

  it('formatManilaDateTime', () => {
    expect(formatManilaDateTime('2026-10-30T06:14:00Z')).toBe('30 Oct 2026, 2:14 PM');
  });

  describe('manilaDayRange', () => {
    it('both', () => {
      expect(manilaDayRange('2026-10-30', '2026-10-30')).toEqual({
        postedFrom: '2026-10-29T16:00:00.000Z',
        postedTo: '2026-10-30T16:00:00.000Z',
      });
    });
    it('only from', () => {
      expect(manilaDayRange('2026-10-30', null)).toEqual({
        postedFrom: '2026-10-29T16:00:00.000Z',
      });
    });
    it('only to', () => {
      expect(manilaDayRange(null, '2026-10-30')).toEqual({
        postedTo: '2026-10-30T16:00:00.000Z',
      });
    });
    it('neither', () => {
      expect(manilaDayRange(null, null)).toEqual({});
      expect(manilaDayRange('', '')).toEqual({});
    });
    it('rolls over month and year', () => {
      expect(manilaDayRange(null, '2026-12-31')).toEqual({
        postedTo: '2026-12-31T16:00:00.000Z',
      });
    });
    it('never emits an offset', () => {
      const r = manilaDayRange('2026-01-01', '2026-01-02');
      expect(JSON.stringify(r)).not.toContain('+');
    });
  });

  it('documentTypeLabel', () => {
    expect(documentTypeLabel('Receipt')).toBe('Received');
    expect(documentTypeLabel('Issue')).toBe('Used or sold');
    expect(documentTypeLabel('Transfer')).toBe('Moved');
    expect(documentTypeLabel('Adjustment')).toBe('Corrected');
    expect(documentTypeLabel('Reversal')).toBe('Reversal');
  });

  it('reasonLabel and ADJUSTMENT_REASONS', () => {
    expect(ADJUSTMENT_REASONS).toEqual(['Damaged', 'Lost', 'Found', 'CountCorrection', 'Other']);
    expect(reasonLabel('CountCorrection')).toBe('Count correction');
    expect(reasonLabel('Damaged')).toBe('Damaged');
    expect(reasonLabel('Other')).toBe('Other');
  });

  it('postedByLabel', () => {
    const me = 'abcdef00-0000-0000-0000-000000000001';
    expect(postedByLabel(SYSTEM_USER_ID, me)).toBe('System');
    expect(postedByLabel(me, me)).toBe('You');
    expect(postedByLabel(me, me.toUpperCase())).toBe('You');
    expect(postedByLabel('other', me)).toBe('Another user');
    expect(postedByLabel(me, null)).toBe('Another user');
  });

  describe('stockStatus', () => {
    it('negative', () => {
      expect(stockStatus(-0.0001)).toEqual({ tone: 'bad', label: 'Negative' });
      expect(stockStatus(-5)).toEqual({ tone: 'bad', label: 'Negative' });
    });
    it('zero', () => {
      expect(stockStatus(0)).toEqual({ tone: 'neutral', label: 'Out of stock' });
    });
    it('positive', () => {
      expect(stockStatus(0.0001)).toEqual({ tone: 'good', label: 'In stock' });
      expect(stockStatus(12)).toEqual({ tone: 'good', label: 'In stock' });
    });
  });

  describe('documentStatus', () => {
    it('posted', () => {
      expect(documentStatus({ reversesDocumentId: null, reversedByDocumentId: null })).toEqual({
        tone: 'good',
        label: 'Posted',
      });
    });
    it('reversed', () => {
      expect(documentStatus({ reversesDocumentId: null, reversedByDocumentId: 'r' })).toEqual({
        tone: 'warn',
        label: 'Reversed',
      });
    });
    it('reverses another', () => {
      expect(documentStatus({ reversesDocumentId: 'd', reversedByDocumentId: null })).toEqual({
        tone: 'neutral',
        label: 'Reverses another',
      });
    });
    it('reversedBy wins over reverses', () => {
      expect(documentStatus({ reversesDocumentId: 'd', reversedByDocumentId: 'r' })).toEqual({
        tone: 'warn',
        label: 'Reversed',
      });
    });
  });
});
