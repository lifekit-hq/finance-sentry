import {type FlowBreakdownItem} from '../models/flow-breakdown/flow-breakdown.model';
import {type GlobalTransactionDto} from '../models/transaction/transaction.model';

const MONTH_KEY_LENGTH = 7;
const DAY_PAD = 2;

/** Helpers for turning the flow breakdown's rows and window into ledger drill-down inputs. */
export class FlowBreakdownUtils {
  /**
   * The ledger-shaped transaction behind a breakdown row, so the existing transaction drawer
   * can open it. The breakdown carries no pending flag or posting date, so the row is shown
   * as posted on its date.
   */
  public static toTransaction(item: FlowBreakdownItem): GlobalTransactionDto {
    return {
      transactionId: item.transactionId,
      accountId: item.accountId,
      bankName: item.bankName,
      currency: item.currency,
      amount: item.amount,
      amountUsd: item.amountUsd,
      date: item.date,
      postedDate: item.date,
      description: item.description,
      transactionType: item.direction === 'in' ? 'credit' : 'debit',
      merchantCategory: item.category,
      isPending: false,
      createdAt: item.date,
    };
  }

  /** The `from`/`to` ledger bounds of a `yyyy-MM` month key; empty for a malformed key. */
  public static monthDates(month: string): {from?: string; to?: string} {
    if (month.length !== MONTH_KEY_LENGTH) {
      return {};
    }
    const [year, monthNumber] = month.split('-').map(Number);
    const lastDay = new Date(Date.UTC(year ?? 0, monthNumber ?? 1, 0)).getUTCDate();
    return {from: `${month}-01`, to: `${month}-${String(lastDay).padStart(DAY_PAD, '0')}`};
  }
}
