import {EQUITY_INSTRUMENT_TYPE} from '../constants/position/position.constants';
import {type Position} from '../models/position/position.model';
import {type QuoteDto} from '../models/quote/quote.model';

const PERCENT_SCALE = 100;

export class DayChangeUtils {
  /** Listed stocks only - cash, bonds, funds and crypto rows have no research quote. */
  public static isQuotable(position: Position): boolean {
    return position.instrumentType?.toUpperCase() === EQUITY_INSTRUMENT_TYPE;
  }

  /** Tickers worth quoting, upper-cased and de-duplicated across brokers. */
  public static quotableTickers(positions: Position[]): string[] {
    const tickers = positions
      .filter(p => DayChangeUtils.isQuotable(p))
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
