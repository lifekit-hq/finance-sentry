import {type Institution} from '../../../shared/models/wealth/wealth.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';

const SINGULAR_COUNT = 1;
const SEPARATOR = ' · ';

/** What an institution row on Accounts says it holds, whatever its category; sync state is appended by the page. */
export class InstitutionNavUtils {
  /** Rows that are holdings: a provider's cash lines are not positions. */
  public static positionCount(institution: Institution): number {
    return institution.accounts.filter(account => account.accountType !== 'cash').length;
  }

  /** The cash lines in the base currency, or null when the institution holds none (or none could be converted). */
  public static cashTotal(institution: Institution): Nullable<number> {
    const cash = institution.accounts.filter(
      account => account.accountType === 'cash' && account.balanceInBaseCurrency !== null
    );
    if (cash.length === 0) {
      return null;
    }
    return cash.reduce((sum, account) => sum + (account.balanceInBaseCurrency ?? 0), 0);
  }

  /** "3 accounts" or "2 cards" for a bank (a card group counts as a card), "14 positions" for a broker. */
  public static countLabel(institution: Institution, noun: string): string {
    const cards = institution.cards?.length ?? 0;
    const count = cards || institution.accounts.length;
    return InstitutionNavUtils.pluralise(count, cards ? 'card' : noun);
  }

  /**
   * One line for every category: what the row holds. A bank says how many accounts or cards; a broker or
   * exchange says "14 positions · $3,008 cash", with the cash part only when there is cash.
   */
  public static sublabel(institution: Institution, noun: string, baseCurrency: string): string {
    if (institution.category === 'banking') {
      return InstitutionNavUtils.countLabel(institution, noun);
    }
    const parts = [
      InstitutionNavUtils.pluralise(InstitutionNavUtils.positionCount(institution), noun),
    ];
    const cash = InstitutionNavUtils.cashTotal(institution);
    if (cash !== null) {
      parts.push(`${MoneyUtils.format(cash, baseCurrency)} cash`);
    }
    return parts.join(SEPARATOR);
  }

  private static pluralise(count: number, noun: string): string {
    return `${count} ${noun}${count === SINGULAR_COUNT ? '' : 's'}`;
  }
}
