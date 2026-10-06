import {
  type TransactionFilterInputText,
  type TransactionFilters,
  type TransactionType,
} from '../models/transaction-filters/transaction-filters.model';

export type CommittedInputFilters = Pick<TransactionFilters, 'minAmount' | 'maxAmount' | 'search'>;

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;

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

  /** Reads a `type` URL value: anything but `credit`/`debit` is no filter. */
  public static parseType(raw: Nullable<string>): Nullable<TransactionType> {
    return raw === 'credit' || raw === 'debit' ? raw : null;
  }

  /** Reads a `yyyy-MM-dd` URL value: any other shape is no bound. */
  public static parseDate(raw: Nullable<string>): Nullable<string> {
    return raw !== null && ISO_DATE.test(raw) ? raw : null;
  }

  public static formatAmount(value: Nullable<number>): string {
    return value === null ? '' : String(value);
  }

  /**
   * Input text after the committed filters changed. Only a field whose committed value changed is
   * touched: text that still means it is kept verbatim (so "1.0" is not rewritten to "1" mid-edit),
   * anything else follows the filters. Fields still waiting on their own debounce keep their text.
   */
  public static syncInputText(
    committed: CommittedInputFilters,
    previous?: {committed: CommittedInputFilters; text: TransactionFilterInputText}
  ): TransactionFilterInputText {
    const amountText = (bound: 'minAmount' | 'maxAmount'): string => {
      if (!previous || previous.committed[bound] === committed[bound]) {
        return previous
          ? previous.text[bound]
          : TransactionFiltersUtils.formatAmount(committed[bound]);
      }
      return TransactionFiltersUtils.parseAmount(previous.text[bound]) === committed[bound]
        ? previous.text[bound]
        : TransactionFiltersUtils.formatAmount(committed[bound]);
    };
    const searchText = (): string => {
      if (!previous || previous.committed.search === committed.search) {
        return previous ? previous.text.search : committed.search;
      }
      return previous.text.search.trim() === committed.search.trim()
        ? previous.text.search
        : committed.search;
    };
    return {
      minAmount: amountText('minAmount'),
      maxAmount: amountText('maxAmount'),
      search: searchText(),
    };
  }
}
