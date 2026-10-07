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

  /**
   * The ledger search that lands on a row's counterparty, or null when its name appears in none of
   * the text the ledger searches: counterparties match by their own rules, so the name is a search
   * term only when this row's merchant or description spells it.
   */
  public static counterpartyQuery(
    item: Pick<FlowBreakdownItem, 'counterpartyName' | 'merchantName' | 'description'>
  ): Nullable<string> {
    const name = item.counterpartyName?.trim();
    if (!name) {
      return null;
    }
    const needle = name.toLowerCase();
    const spelled = [item.merchantName, item.description].some(text =>
      text?.toLowerCase().includes(needle)
    );
    return spelled ? name : null;
  }
}
