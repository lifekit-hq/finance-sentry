import {type Nullable} from '@lifekit-hq/ui';

/** How much of the sleeve a P&L figure covers (`GET /wealth/brokerage/pnl`). */
export type PnlStatus = 'COMPLETE' | 'PARTIAL' | 'UNAVAILABLE';

/**
 * One side of the split. `usd` is in `currency` (the base currency, despite the name) and null when
 * `status` is UNAVAILABLE; `percentOfNetDeposited` is percentage points and only set for a COMPLETE figure.
 * `measured` / `total` count positions (unrealized) or symbols that sold in the window (realized);
 * `unavailableSymbols` names what is left out.
 */
export interface PnlFigure {
  usd: Nullable<number>;
  percentOfNetDeposited: Nullable<number>;
  status: PnlStatus;
  measured: number;
  total: number;
  coverage: Nullable<number>;
  unavailableSymbols: string[];
}

/**
 * The brokerage sleeve's realized and unrealized P&L on its net deposited, over the growth split's window.
 * `netDepositedUsd` is null while a provider's deposits are unknown (`sourcesWithoutFlows` names it).
 */
export interface BrokeragePnl {
  from: Nullable<string>;
  to: Nullable<string>;
  netDepositedUsd: Nullable<number>;
  sourcesWithoutFlows: string[];
  realized: PnlFigure;
  unrealized: PnlFigure;
  currency: string;
}

export type PnlTone = 'gain' | 'loss' | 'neutral';

/** One header row, ready to render: an amount or the honest reason there is none. */
export interface PnlRow {
  key: 'net-deposited' | 'realized' | 'unrealized';
  label: string;
  /** The amount in `currency`; null renders the `unavailable` text instead. */
  amount: Nullable<number>;
  /** Signed percent of the net deposited, e.g. "+10.9%"; null unless the figure is complete. */
  percentLabel: Nullable<string>;
  /** A few words under the label: what the figure rests on (short enough for a phone row). */
  note: string;
  /** The full sentence behind a partial or withheld figure, naming what is left out; null when complete. */
  detail: Nullable<string>;
  /** Shown in place of an amount when there is none. */
  unavailable: Nullable<string>;
  tone: PnlTone;
  signed: boolean;
}
