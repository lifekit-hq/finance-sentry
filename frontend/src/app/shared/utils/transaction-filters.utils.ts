import {type TransactionFilters} from '../models/transaction-filters/transaction-filters.model';

export class TransactionFiltersUtils {
  /** True when any dimension narrows the list. */
  public static isActive(filters: TransactionFilters): boolean {
    return (
      filters.accountIds.length > 0 ||
      filters.categories.length > 0 ||
      filters.transactionType !== null ||
      filters.from !== null ||
      filters.to !== null ||
      filters.minAmount !== null ||
      filters.maxAmount !== null ||
      filters.search.trim() !== ''
    );
  }

  /** Parses an amount input: blank or non-numeric text clears the bound. */
  public static parseAmount(raw: Nullable<string>): Nullable<number> {
    if (raw === null || raw === undefined || raw.trim() === '') {
      return null;
    }
    const value = Number(raw);
    return Number.isFinite(value) ? value : null;
  }
}
