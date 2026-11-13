import { formatCurrency, formatDate, formatNumber } from '@angular/common';

const LOCALE = 'en-US';

export type DocumentType = 'Receipt' | 'Issue' | 'Transfer' | 'Adjustment' | 'Reversal';
export type AdjustmentReason = 'Damaged' | 'Lost' | 'Found' | 'CountCorrection' | 'Other';

export const ADJUSTMENT_REASONS: readonly AdjustmentReason[] = [
  'Damaged',
  'Lost',
  'Found',
  'CountCorrection',
  'Other',
];

/** The seeded S5d postings carry the all-zero user id. */
export const SYSTEM_USER_ID = '00000000-0000-0000-0000-000000000000';

export function formatMoney(value: number): string {
  return formatCurrency(value, LOCALE, '₱', 'PHP', '1.2-2');
}

export function formatQuantity(value: number): string {
  return formatNumber(value, LOCALE, '1.0-4');
}

/** Asia/Manila has no DST, so a fixed +0800 offset is exact. */
export function formatManilaDateTime(iso: string): string {
  return formatDate(iso, 'd MMM y, h:mm a', LOCALE, '+0800');
}

function manilaMidnightUtc(day: string, addDays: number): string {
  const [y, m, d] = day.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d + addDays) - 8 * 3_600_000).toISOString();
}

/**
 * Manila calendar days (yyyy-mm-dd, `to` inclusive) to a half-open UTC range.
 * Always emits `Z` instants: a `+08:00` would be read as a space in the query string.
 */
export function manilaDayRange(
  from: string | null,
  to: string | null,
): { postedFrom?: string; postedTo?: string } {
  const range: { postedFrom?: string; postedTo?: string } = {};
  if (from) {
    range.postedFrom = manilaMidnightUtc(from, 0);
  }
  if (to) {
    range.postedTo = manilaMidnightUtc(to, 1);
  }
  return range;
}

export function documentTypeLabel(type: DocumentType): string {
  switch (type) {
    case 'Receipt':
      return 'Received';
    case 'Issue':
      return 'Used or sold';
    case 'Transfer':
      return 'Moved';
    case 'Adjustment':
      return 'Corrected';
    case 'Reversal':
      return 'Reversal';
  }
}

export function reasonLabel(reason: AdjustmentReason): string {
  return reason === 'CountCorrection' ? 'Count correction' : reason;
}

export function postedByLabel(postedBy: string, currentUserId: string | null | undefined): string {
  if (postedBy === SYSTEM_USER_ID) {
    return 'System';
  }
  if (currentUserId && postedBy.toLowerCase() === currentUserId.toLowerCase()) {
    return 'You';
  }
  return 'Another user';
}
