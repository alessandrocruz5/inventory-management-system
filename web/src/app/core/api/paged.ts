/** Matches Inventory's `PagedResponse<T>` (spec §9 lists). */
export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}
