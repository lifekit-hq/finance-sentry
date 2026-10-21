import {type Nullable} from '@lifekit-hq/ui';

import {type Headline} from '../../../shared/models/headline/headline.model';
import {MoneyUtils} from '../../../shared/utils/money.utils';
import {
  NET_WORTH_BRIDGE_LABELS,
  NET_WORTH_BRIDGE_METHOD,
  NET_WORTH_BRIDGE_RETURN_PARTS,
  NET_WORTH_BRIDGE_SCALE_PADDING,
  NET_WORTH_BRIDGE_SCOPE,
  NET_WORTH_BRIDGE_SUMMARY_EXPLANATIONS,
  NET_WORTH_BRIDGE_SUMMARY_LABELS,
  NET_WORTH_BRIDGE_SUMMARY_NEGLIGIBLE,
} from '../constants/net-worth-bridge/net-worth-bridge.constants';
import {
  type NetWorthBridgeResponse,
  type NetWorthBridgeStep,
  type NetWorthBridgeStepKind,
  type NetWorthBridgeSummary,
} from '../models/net-worth-bridge/net-worth-bridge.model';

const PERCENT = 100;
const MIDDLE = 50;

type LineKey = Exclude<keyof typeof NET_WORTH_BRIDGE_LABELS, 'start' | 'end'>;

interface Line {
  key: LineKey;
  amount: number;
  detail: Nullable<string>;
}

export class NetWorthBridgeUtils {
  /**
   * End − Start as a headline carrying the bridge's quality and reasons, for its badge; the amount is
   * null when either end is (an unavailable bridge never reads as a zero change).
   */
  public static headline(bridge: NetWorthBridgeResponse): Headline {
    const {startValue, endValue} = bridge;
    return {
      amount: startValue == null || endValue == null ? null : endValue - startValue,
      percent: null,
      method: NET_WORTH_BRIDGE_METHOD,
      scope: NET_WORTH_BRIDGE_SCOPE,
      window: {from: bridge.from, to: bridge.to ?? ''},
      quality: bridge.quality,
      reasons: bridge.reasons,
      currency: bridge.currency,
    };
  }

  /**
   * The waterfall rows (finance-framework P4): the start, each line in the order 3.3 names them, the
   * unexplained residual (always, even at zero) and the end. Savings, investment return and currency
   * moves always show; revaluation and the link lines only when they moved anything. Each line's bar runs
   * from the total before it to the total after it on one scale spanning every running total, so a small
   * line next to a large net worth stays visible; the ends are drawn as markers, not bars from zero. Empty
   * when the bridge has no figures.
   */
  public static steps(bridge: NetWorthBridgeResponse): NetWorthBridgeStep[] {
    const {startValue, endValue} = bridge;
    if (startValue == null || endValue == null) {
      return [];
    }
    const lines = NetWorthBridgeUtils.lines(bridge);
    const totals = lines.reduce(
      (acc, line) => [...acc, acc[acc.length - 1] + line.amount],
      [startValue]
    );
    const scale = NetWorthBridgeUtils.scale([...totals, endValue]);
    const total = (key: 'start' | 'end', amount: number): NetWorthBridgeStep => ({
      key,
      label: NET_WORTH_BRIDGE_LABELS[key],
      amount,
      kind: 'total',
      offset: scale(amount),
      width: 0,
      detail: null,
    });

    return [
      total('start', startValue),
      ...lines.map((line, i): NetWorthBridgeStep => {
        const [from, to] = [scale(totals[i]), scale(totals[i + 1])];
        return {
          key: line.key,
          label: NET_WORTH_BRIDGE_LABELS[line.key],
          amount: line.amount,
          kind: NetWorthBridgeUtils.kind(line),
          offset: Math.min(from, to),
          width: Math.abs(to - from),
          detail: line.detail,
        };
      }),
      total('end', endValue),
    ];
  }

  /**
   * Home's three lines (you saved, markets, exchange rates) and the change, End − Start. The change
   * keeps every other line folded in, so the three need not add up to it; `changeNote` names the gap.
   * Null when the bridge has no figures. Presentation only: the amounts are the bridge's own.
   */
  public static summary(bridge: NetWorthBridgeResponse): Nullable<NetWorthBridgeSummary> {
    const {startValue, endValue, currency} = bridge;
    if (startValue == null || endValue == null) {
      return null;
    }
    const savings = bridge.savings ?? 0;
    const investmentReturn = bridge.investmentReturn ?? 0;
    const fxTranslation = bridge.fxTranslation ?? 0;
    const change = endValue - startValue;
    const other = change - savings - investmentReturn - fxTranslation;
    return {
      lines: [
        {
          key: 'savings',
          label: NET_WORTH_BRIDGE_SUMMARY_LABELS.savings,
          amount: savings,
          explanation: NET_WORTH_BRIDGE_SUMMARY_EXPLANATIONS.savings,
        },
        {
          key: 'investmentReturn',
          label: NET_WORTH_BRIDGE_SUMMARY_LABELS.investmentReturn,
          amount: investmentReturn,
          explanation: NET_WORTH_BRIDGE_SUMMARY_EXPLANATIONS.investmentReturn,
        },
        {
          key: 'fxTranslation',
          label: NET_WORTH_BRIDGE_SUMMARY_LABELS.fxTranslation,
          amount: fxTranslation,
          explanation: NetWorthBridgeUtils.exchangeRateSentence(fxTranslation, currency),
        },
      ],
      change,
      changeNote:
        Math.abs(other) < NET_WORTH_BRIDGE_SUMMARY_NEGLIGIBLE
          ? null
          : `Includes ${MoneyUtils.format(other, currency, {maxFractionDigits: 0, signed: true})} from accounts linked or removed, revaluations and moves not yet explained.`,
      currency,
    };
  }

  /** Why exchange rates move net worth with nothing done: other-currency accounts re-read in `currency`. */
  private static exchangeRateSentence(amount: number, currency: string): string {
    if (Math.abs(amount) < NET_WORTH_BRIDGE_SUMMARY_NEGLIGIBLE) {
      return `Accounts in other currencies are counted in ${currency}; rates did not move them.`;
    }
    const direction = amount > 0 ? 'more' : 'less';
    return `Rates moved, so your accounts in other currencies are worth ${direction} in ${currency}, though you did nothing.`;
  }

  private static lines(bridge: NetWorthBridgeResponse): Line[] {
    const line = (
      key: LineKey,
      amount: Nullable<number>,
      detail: Nullable<string> = null
    ): Line => ({
      key,
      amount: amount ?? 0,
      detail,
    });
    const optional = [
      line('revaluation', bridge.revaluation),
      line('newlyLinked', bridge.newlyLinked),
      line('unlinked', -(bridge.unlinked ?? 0)),
    ].filter(l => l.amount !== 0);

    return [
      line('savings', bridge.savings),
      line('investmentReturn', bridge.investmentReturn, NetWorthBridgeUtils.returnDetail(bridge)),
      line('fxTranslation', bridge.fxTranslation),
      ...optional,
      line('residual', bridge.residual),
    ];
  }

  /**
   * "market +$900.00 · income +$40.00 · fees -$12.00" when income, fees or taxes moved the return; null
   * when the market is all of it. Fees and taxes are subtracted, so they read negative.
   */
  private static returnDetail(bridge: NetWorthBridgeResponse): Nullable<string> {
    const parts = [
      {label: NET_WORTH_BRIDGE_RETURN_PARTS.income, amount: bridge.income ?? 0},
      {label: NET_WORTH_BRIDGE_RETURN_PARTS.fees, amount: -(bridge.fees ?? 0)},
      {label: NET_WORTH_BRIDGE_RETURN_PARTS.taxes, amount: -(bridge.taxes ?? 0)},
    ].filter(p => p.amount !== 0);
    if (parts.length === 0) {
      return null;
    }
    return [{label: NET_WORTH_BRIDGE_RETURN_PARTS.market, amount: bridge.market ?? 0}, ...parts]
      .map(p => `${p.label} ${MoneyUtils.format(p.amount, bridge.currency, {signed: true})}`)
      .join(' · ');
  }

  private static kind(line: Line): NetWorthBridgeStepKind {
    if (line.key === 'residual') {
      return 'residual';
    }
    if (line.amount === 0) {
      return 'flat';
    }
    return line.amount > 0 ? 'gain' : 'loss';
  }

  /** Maps a value to its percentage across the padded span of `values`; the middle when they are all equal. */
  private static scale(values: number[]): (value: number) => number {
    const low = Math.min(...values);
    const span = Math.max(...values) - low;
    if (span === 0) {
      return () => MIDDLE;
    }
    const padding = span * NET_WORTH_BRIDGE_SCALE_PADDING;
    const padded = span + padding + padding;
    return value => ((value - low + padding) / padded) * PERCENT;
  }
}
