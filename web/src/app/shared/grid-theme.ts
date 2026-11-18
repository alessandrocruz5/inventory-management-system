import { themeQuartz } from 'ag-grid-community';

/** Shared AG Grid look for Stock and Documents (handoff README "Tables"). */
export const appGridTheme = themeQuartz.withParams({
  fontFamily: 'IBM Plex Sans',
  fontSize: 14,
  headerFontSize: 12,
  headerFontWeight: 500,
  headerTextColor: '#78716C',
  headerBackgroundColor: '#FAFAF9',
  backgroundColor: '#FFFFFF',
  foregroundColor: '#1C1B19',
  borderColor: '#E7E5E2',
  rowBorder: { color: '#F0EFED' },
  headerRowBorder: { color: '#E7E5E2' },
  columnBorder: false,
  wrapperBorder: false,
  wrapperBorderRadius: 0,
  cellHorizontalPadding: 16,
  rowHoverColor: '#FAFAF9',
  selectedRowBackgroundColor: '#F2F1EF',
  accentColor: '#1C1B19',
  headerHeight: 41,
  rowHeight: 48,
});
