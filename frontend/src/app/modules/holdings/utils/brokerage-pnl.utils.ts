import {type Nullable} from '@lifekit-hq/ui';

import {PercentUtils} from '../../../shared/utils/percent.utils';
import {ProviderUtils} from '../../../shared/utils/provider.utils';
import {
  BASIS_UNAVAILABLE,
  CAPITAL_IN_WINDOW_DETAIL,
  COVERAGE_PERCENT,
  MAX_SYMBOLS_LISTED,
  PNL_PERCENT_DIGITS,
} from '../constants/brokerage-pnl/brokerage-pnl.constants';
import {
  type BrokeragePnl,
  type PnlFigure,
  type PnlRow,
  type PnlTone,
} from '../models/brokerage-pnl/brokerage-pnl.model';

export class BrokeragePnlUtils {
  /**
   * The header's three rows in reading order. A figure that cannot be stated is a row with no amount and the
   * reason in its note, never a zero; a partial one keeps its amount, drops its percent (a part of the book is
   * not put over the whole book's capital) and says what it covers.
   */
  public static rows(pnl: BrokeragePnl): PnlRow[] {
    return [
      BrokeragePnlUtils.netDepositedRow(pnl),
      BrokeragePnlUtils.realizedRow(pnl.realized),
      BrokeragePnlUtils.unrealizedRow(pnl.unrealized),
    ];
  }

  private static netDepositedRow(pnl: BrokeragePnl): PnlRow {
    const missing = pnl.sourcesWithoutFlows.map(slug => ProviderUtils.label(slug)).join(' and ');
    const hasAmount = pnl.netDepositedUsd != null;
    let detail: Nullable<string> = null;
    if (hasAmount) {
      detail = CAPITAL_IN_WINDOW_DETAIL;
    } else if (missing !== '') {
      detail = `Capital in the window: no deposits or withdrawals recorded yet for ${missing}, so none is stated.`;
    }
    return {
      key: 'net-deposited',
      label: 'Capital in the window',
      amount: pnl.netDepositedUsd,
      percentLabel: null,
      note: hasAmount ? 'Opening value + deposits less withdrawals' : 'Not measured yet',
      detail,
      unavailable: hasAmount ? null : 'Not measured',
      tone: 'neutral',
      signed: false,
    };
  }

  private static realizedRow(figure: PnlFigure): PnlRow {
    let note = '';
    let detail: Nullable<string> = null;
    if (figure.status === 'COMPLETE') {
      note = figure.total === 0 ? 'Nothing sold yet' : 'From your fills';
    } else if (figure.status === 'PARTIAL') {
      note = `Partial: ${figure.measured} of ${figure.total} sold`;
      detail = `Realized P&L leaves out ${BrokeragePnlUtils.symbols(figure.unavailableSymbols)}: your fills do not cover their history.`;
    } else if (figure.unavailableSymbols.length > 0) {
      note = 'Fills incomplete';
      detail = `Realized P&L is not stated: your fills do not cover the history of ${BrokeragePnlUtils.symbols(figure.unavailableSymbols)}.`;
    } else {
      note = 'No fills yet';
    }
    return BrokeragePnlUtils.gainRow('realized', 'Realized P&L', figure, note, detail);
  }

  private static unrealizedRow(figure: PnlFigure): PnlRow {
    let note = '';
    let detail: Nullable<string> = null;
    if (figure.status === 'COMPLETE') {
      note = 'Cost verified from fills';
    } else if (figure.status === 'PARTIAL') {
      const share =
        figure.coverage == null
          ? ''
          : ` · ${Math.round(figure.coverage * COVERAGE_PERCENT)}% of value`;
      note = `Partial: ${figure.measured} of ${figure.total}${share}`;
      detail = `Unrealized P&L covers only the positions whose cost your fills verify; ${BrokeragePnlUtils.symbols(figure.unavailableSymbols)} ${figure.unavailableSymbols.length === 1 ? 'is' : 'are'} left out.`;
    } else {
      note = 'No verified cost basis';
      detail = 'Unrealized P&L is not stated: no position has a cost basis your fills verify.';
    }
    return BrokeragePnlUtils.gainRow('unrealized', 'Unrealized P&L', figure, note, detail);
  }

  private static gainRow(
    key: 'realized' | 'unrealized',
    label: string,
    figure: PnlFigure,
    note: string,
    detail: Nullable<string>
  ): PnlRow {
    const amount = figure.status === 'UNAVAILABLE' ? null : figure.usd;
    return {
      key,
      label,
      amount,
      percentLabel:
        figure.percentOfNetDeposited == null
          ? null
          : PercentUtils.signed(figure.percentOfNetDeposited, PNL_PERCENT_DIGITS),
      note,
      detail,
      unavailable: amount == null ? BASIS_UNAVAILABLE : null,
      tone: BrokeragePnlUtils.tone(amount),
      signed: true,
    };
  }

  private static tone(amount: Nullable<number>): PnlTone {
    if (amount == null || amount === 0) {
      return 'neutral';
    }
    return amount > 0 ? 'gain' : 'loss';
  }

  private static symbols(symbols: string[]): string {
    const listed = symbols.slice(0, MAX_SYMBOLS_LISTED).join(', ');
    const rest = symbols.length - MAX_SYMBOLS_LISTED;
    return rest > 0 ? `${listed} and ${rest} more` : listed;
  }
}
