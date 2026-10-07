import {type Position} from '../models/position/position.model';
import {type QuoteDto} from '../models/quote/quote.model';

const PERCENT_SCALE = 100;

export class DayChangeUtils {
  /** Tickers worth quoting: equities only - crypto assets and venue cash have no research quote. */
  public static quotableTickers(
    positions: Position[],
    cryptoProviders: ReadonlySet<string>
  ): string[] {
    const tickers = positions
      .filter(p => !p.isVenueCash && !cryptoProviders.has(p.provider))
      .map(p => p.symbol.toUpperCase());
    return [...new Set(tickers)];
  }

  /** Day change % keyed by the requested ticker; quotes without a change are left out. */
  public static pctByTicker(quotes: QuoteDto[]): Record<string, number> {
    const result: Record<string, number> = {};
    for (const q of quotes) {
      if (q.changePct != null && Number.isFinite(q.changePct)) {
        result[q.ticker.toUpperCase()] = q.changePct;
      }
    }
    return result;
  }

  /** USD value gained today: the current value minus the same holding at the previous close. */
  public static dayChangeUsd(currentValue: number, changePct: Nullable<number>): Nullable<number> {
    if (changePct == null || changePct <= -PERCENT_SCALE) {
      return null;
    }
    return currentValue - currentValue / (1 + changePct / PERCENT_SCALE);
  }
}
