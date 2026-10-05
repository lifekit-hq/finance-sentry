import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
  type TransactionDayGroup,
} from '../models/transaction/transaction.model';

const DAY_KEY_LENGTH = 10;
const DATE_PART_WIDTH = 2;
const MS_PER_DAY = 86_400_000;
const LABEL_LOCALE = 'en-US';
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
      : TransactionGroupUtils.localDayKey(instant);
  }

  /** "Today", "Yesterday", otherwise "Mon, Aug 10" (the year is added outside the current one). */
  public static dayLabel(dayKey: string, now: Date = new Date()): string {
    const today = TransactionGroupUtils.localDayKey(now);
    if (dayKey === today) {
      return 'Today';
    }
    if (dayKey === TransactionGroupUtils.localDayKey(new Date(now.getTime() - MS_PER_DAY))) {
      return 'Yesterday';
    }
    const date = new Date(`${dayKey}T00:00:00`);
    if (Number.isNaN(date.getTime())) {
      return dayKey;
    }
    const sameYear = date.getFullYear() === now.getFullYear();
    return date.toLocaleDateString(LABEL_LOCALE, {
      weekday: 'short',
      month: 'short',
      day: 'numeric',
      ...(sameYear ? {} : {year: 'numeric'}),
    });
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

  private static localDayKey(date: Date): string {
    const month = String(date.getMonth() + 1).padStart(DATE_PART_WIDTH, '0');
    const day = String(date.getDate()).padStart(DATE_PART_WIDTH, '0');
    return `${date.getFullYear()}-${month}-${day}`;
  }
}
