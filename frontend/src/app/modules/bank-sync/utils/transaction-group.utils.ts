import {DateUtils} from '../../../shared/utils/date.utils';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
  type TransactionDayGroup,
} from '../models/transaction/transaction.model';

const DAY_KEY_LENGTH = 10;
const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

export class TransactionGroupUtils {
  /** Groups a newest-first page into calendar days, keeping the incoming order. */
  public static groupByDay(
    transactions: readonly GlobalTransactionDto[],
    now: Date = new Date()
  ): TransactionDayGroup[] {
    const groups: TransactionDayGroup[] = [];
    const byKey = new Map<string, TransactionDayGroup>();
    for (const tx of transactions) {
      const dayKey = TransactionGroupUtils.dayKey(tx);
      let group = byKey.get(dayKey);
      if (!group) {
        group = {dayKey, label: TransactionGroupUtils.dayLabel(dayKey, now), items: []};
        byKey.set(dayKey, group);
        groups.push(group);
      }
      group.items.push(tx);
    }
    return groups;
  }

  /**
   * Calendar day of a row in the viewer's timezone — the same day the drawer shows. A bare
   * `YYYY-MM-DD` is already a calendar day; a timestamp is an instant (a bank's local midnight
   * arrives as the previous evening in UTC), so slicing its UTC text would shift the row back a day.
   */
  public static dayKey(tx: Pick<GlobalTransactionDto, 'postedDate' | 'date'>): string {
    const raw = tx.postedDate ?? tx.date;
    if (DATE_ONLY.test(raw)) {
      return raw;
    }
    const instant = new Date(raw);
    return Number.isNaN(instant.getTime())
      ? raw.slice(0, DAY_KEY_LENGTH)
      : DateUtils.isoDateLocal(instant);
  }

  /** "Today", "Yesterday", otherwise "Mon 10 Aug" (the year is added outside the current one). */
  public static dayLabel(dayKey: string, now: Date = new Date()): string {
    return DateUtils.recentDayLabel(dayKey, now);
  }

  public static accountLabel(account: {bankName: string; accountNumberLast4: string}): string {
    return account.accountNumberLast4
      ? `${account.bankName} · ${account.accountNumberLast4}`
      : account.bankName;
  }

  public static toAccountOptions(
    accounts: readonly {accountId: string; bankName: string; accountNumberLast4: string}[]
  ): TransactionAccountOption[] {
    return accounts.map(a => ({
      accountId: a.accountId,
      label: TransactionGroupUtils.accountLabel(a),
    }));
  }
}
