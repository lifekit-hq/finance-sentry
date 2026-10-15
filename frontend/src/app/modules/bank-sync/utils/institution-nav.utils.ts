import {type Institution} from '../../../shared/models/wealth/wealth.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';

const SINGULAR_COUNT = 1;
const SEPARATOR = ' · ';

/** What a brokerage or exchange row on Accounts says about its institution, now that its positions live on Investments. */
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

  /** "14 positions · $3,008 cash", with the cash part only when there is cash. */
  public static sublabel(institution: Institution, noun: string, baseCurrency: string): string {
    const count = InstitutionNavUtils.positionCount(institution);
    const parts = [`${count} ${noun}${count === SINGULAR_COUNT ? '' : 's'}`];
    const cash = InstitutionNavUtils.cashTotal(institution);
    if (cash !== null) {
      parts.push(`${MoneyUtils.format(cash, baseCurrency)} cash`);
    }
    return parts.join(SEPARATOR);
  }
}
