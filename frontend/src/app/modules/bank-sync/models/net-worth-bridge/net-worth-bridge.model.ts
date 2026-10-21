import {type Nullable} from '@lifekit-hq/ui';

import {
  type HeadlineQuality,
  type HeadlineReason,
} from '../../../../shared/models/headline/headline.model';

/**
 * `GET /net-worth/bridge` (finance-framework M5): End − Start between the first and last snapshot of a
 * window, split into lines that add up exactly, in `currency`. Every amount is null when the window holds
 * fewer than two snapshots (UNAVAILABLE, `SHORT_HISTORY`), never 0 in its place.
 */
export interface NetWorthBridgeResponse {
  /** ISO dates of the opening and closing snapshots. */
  from: Nullable<string>;
  to: Nullable<string>;
  startValue: Nullable<number>;
  endValue: Nullable<number>;
  savings: Nullable<number>;
  /** Market + income − fees − taxes. */
  investmentReturn: Nullable<number>;
  market: Nullable<number>;
  income: Nullable<number>;
  fees: Nullable<number>;
  taxes: Nullable<number>;
  fxTranslation: Nullable<number>;
  revaluation: Nullable<number>;
  /** Value brought in by a link inside the window, as a positive amount. */
  newlyLinked: Nullable<number>;
  /** Value taken out by a removal inside the window, as a positive amount the bridge subtracts. */
  unlinked: Nullable<number>;
  /** End − Start − every other line. */
  residual: Nullable<number>;
  residualAboveTolerance: boolean;
  quality: HeadlineQuality;
  reasons: HeadlineReason[];
  currency: string;
}

/** A waterfall row: an end (a total) or a line moving the running total. */
export type NetWorthBridgeStepKind = 'total' | 'gain' | 'loss' | 'flat' | 'residual';

export interface NetWorthBridgeStep {
  key: string;
  label: string;
  /** The total for an end; the signed move for a line. */
  amount: number;
  kind: NetWorthBridgeStepKind;
  /** Where the bar starts, as a percentage of the shared scale's width. */
  offset: number;
  /** The bar's width as a percentage of the scale; 0 for a line that does not move the total. */
  width: number;
  /** The parts behind a line ("market +$900 · income +$40 · fees −$12"); null when it has none. */
  detail: Nullable<string>;
}

/** The DEGRADED state's reason, shown in place: the registry's reason and how far the residual went. */
export interface NetWorthBridgeDegraded {
  reason: string;
  detail: string;
}

/** One of Home's three "why it moved" lines, with its sentence in plain words. */
export interface NetWorthBridgeSummaryLine {
  key: 'savings' | 'investmentReturn' | 'fxTranslation';
  label: string;
  amount: number;
  explanation: string;
}

/**
 * Home's plain reading of the bridge: three lines and the change. Revaluation, accounts linked or removed
 * and the unexplained residual fold into the change (`changeNote` names them); the full statement stays on
 * Breakdown.
 */
export interface NetWorthBridgeSummary {
  lines: NetWorthBridgeSummaryLine[];
  change: number;
  /** What the other bridge lines added to the change; null when they moved nothing worth naming. */
  changeNote: Nullable<string>;
  currency: string;
}
