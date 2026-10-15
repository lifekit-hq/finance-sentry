import {type AccountBalanceItem} from '../../../shared/models/wealth/wealth.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';

const MAX_QUANTITY_FRACTION_DIGITS = 8;
const SHARE_PROVIDERS: ReadonlySet<string> = new Set(['ibkr']);
const SINGULAR_QUANTITY = 1;

export class PositionUtils {
  /** The position's value in the base currency, the primary figure of a position row. */
  public static value(account: AccountBalanceItem, baseCurrency: string): string {
    if (account.balanceInBaseCurrency !== null) {
      return MoneyUtils.format(account.balanceInBaseCurrency, baseCurrency);
    }
    const hasNative = account.nativeValue !== null && account.nativeValue !== undefined;
    return hasNative
      ? MoneyUtils.format(account.nativeValue, account.nativeCurrency)
      : MoneyUtils.format(null);
  }

  /** The position's size in units, never as money: "40 shares", "0.0812 BTC". */
  public static quantity(account: AccountBalanceItem): string {
    const amount = new Intl.NumberFormat('en-US', {
      maximumFractionDigits: MAX_QUANTITY_FRACTION_DIGITS,
    }).format(account.currentBalance);
    const symbol = account.symbol ?? account.currency;
    if (account.category === 'crypto') {
      return `${amount} ${symbol}`;
    }
    const noun = SHARE_PROVIDERS.has(account.provider) ? 'share' : 'unit';
    const plural = account.currentBalance === SINGULAR_QUANTITY ? '' : 's';
    return `${amount} ${noun}${plural}`;
  }
}
