import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {type NetWorthBridgeStepKind} from '../../models/net-worth-bridge/net-worth-bridge.model';

/** The bridge headline's method and scope: every part of net worth (finance-framework M5). */
export const NET_WORTH_BRIDGE_METHOD = 'BRIDGE';
export const NET_WORTH_BRIDGE_SCOPE = 'umbrella';

/** The share of the start value above which the residual degrades the bridge (finance-framework 3.3). */
export const NET_WORTH_BRIDGE_RESIDUAL_TOLERANCE_LABEL = '0.2%';

/** Waterfall rows, in the order finance-framework 3.3 names the lines. */
export const NET_WORTH_BRIDGE_LABELS = {
  start: 'Start',
  savings: 'Savings',
  investmentReturn: 'Investment return',
  fxTranslation: 'Currency moves',
  revaluation: 'Revaluation',
  newlyLinked: 'Newly linked',
  unlinked: 'Unlinked',
  residual: 'Unexplained',
  end: 'End',
} as const;

/** Home's three "why it moved" lines and the change they add up to, in plain words. */
export const NET_WORTH_BRIDGE_SUMMARY_LABELS = {
  savings: 'You saved',
  investmentReturn: 'Markets',
  fxTranslation: 'Exchange rates',
  change: 'Change',
} as const;

/** The one sentence under each "why it moved" line. */
export const NET_WORTH_BRIDGE_SUMMARY_EXPLANATIONS = {
  savings: 'Money that came in minus money you spent.',
  investmentReturn: 'Price moves on your investments, plus dividends and interest, after fees.',
} as const;

/** Under the three lines when the bridge is not exact. */
export const NET_WORTH_BRIDGE_ESTIMATE_NOTE =
  'These figures are estimates: some history or exchange rates are missing or stale.';

/** Under 1 unit of the currency, a move is too small to name: no "other" note, no rate move. */
export const NET_WORTH_BRIDGE_SUMMARY_NEGLIGIBLE = 1;

/** The parts of the investment return its row names under it. */
export const NET_WORTH_BRIDGE_RETURN_PARTS = {
  market: 'market',
  income: 'income',
  fees: 'fees',
  taxes: 'taxes',
} as const;

/** Each bar keeps this much of the scale's width as margin on either side. */
export const NET_WORTH_BRIDGE_SCALE_PADDING = 0.05;

export const NET_WORTH_BRIDGE_DEFAULT_ERROR = 'Failed to load why your net worth changed.';

/** Shown when the bridge is unavailable for a reason the registry does not name. */
export const NET_WORTH_BRIDGE_UNAVAILABLE_FALLBACK =
  'Not enough history in this period to explain the change yet.';

/** Each row's bar: a gain or loss in its colour, a line that moved nothing and the residual muted. */
export const NET_WORTH_BRIDGE_BAR_CLASS: Record<NetWorthBridgeStepKind, string> = {
  total: 'bg-text-primary',
  gain: 'bg-gain',
  loss: 'bg-loss',
  flat: 'bg-border-strong',
  residual: 'bg-border-strong',
};

/** Each row's amount, in the same colour as its bar. */
export const NET_WORTH_BRIDGE_AMOUNT_CLASS: Record<NetWorthBridgeStepKind, string> = {
  total: 'font-semibold text-text-primary',
  gain: GAIN_TEXT_CLASS,
  loss: LOSS_TEXT_CLASS,
  flat: 'text-text-secondary',
  residual: 'text-text-secondary',
};

/** The residual's bar and amount once it is above tolerance: the warning colour the DEGRADED alert wears. */
export const NET_WORTH_BRIDGE_DEGRADED_BAR_CLASS = 'bg-status-warning';
export const NET_WORTH_BRIDGE_DEGRADED_AMOUNT_CLASS = 'font-semibold text-status-warning';

/** The reason code a residual above tolerance carries (finance-framework 3.3). */
export const NET_WORTH_BRIDGE_DEGRADED_REASON = 'RESIDUAL_ABOVE_TOLERANCE';
export const NET_WORTH_BRIDGE_DEGRADED_FALLBACK =
  'Part of the change in net worth is not explained by known movements.';
