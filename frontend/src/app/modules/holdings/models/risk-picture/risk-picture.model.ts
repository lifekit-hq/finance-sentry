import {type Nullable} from '@lifekit-hq/ui';

/** A measured rate (a fraction) or the typed reason there is none. */
export interface RiskFigure {
  value: Nullable<number>;
  reason: Nullable<string>;
}

/** `GET /radar/risk-picture`: the M8 figures, the largest weights against the stored position flag, and the tolerance. */
export interface RiskPicture {
  from: Nullable<string>;
  to: Nullable<string>;
  monthsObserved: number;
  betaMonths: number;
  volatility: RiskFigure;
  maxDrawdown: RiskFigure;
  beta: RiskFigure;
  drawdownTolerance: Nullable<number>;
  top1Symbol: Nullable<string>;
  top1Weight: Nullable<number>;
  top3Symbols: string[];
  top3Weight: Nullable<number>;
  cryptoShare: Nullable<number>;
  positionFlag: Nullable<number>;
  top1OverFlag: Nullable<boolean>;
}

/** One row of the panel. Every row carries a plain explanation and the basis its number was measured on. */
export interface RiskFigureRow {
  key: string;
  label: string;
  value: string;
  explanation: string;
  basis: string;
  /** Set when the row's own rule is breached (the position flag); read as a warning. */
  warning: boolean;
}
