import {type Nullable} from '@lifekit-hq/ui';

export interface ThesisDataPoint {
  metric: string;
  direction: string;
  threshold: number;
  source: string;
}

export interface ThesisCatalyst {
  date: string;
  event: string;
}

export interface ThesisInvalidationTrigger {
  metric: string;
  direction: string;
  threshold: number;
  proxyTicker: Nullable<string>;
  consecutivePeriods: number;
  periodType: string;
  benchmarkTicker?: Nullable<string>;
  windowDays?: Nullable<number>;
}

export interface ThesisDto {
  id: string;
  ticker: string;
  thesisText: string;
  keyDataPoints: ThesisDataPoint[];
  catalysts: ThesisCatalyst[];
  invalidationTriggers: ThesisInvalidationTrigger[];
  createdAt: string;
  updatedAt: string;
  brokenAt: Nullable<string>;
  brokenReason: Nullable<string>;
  entryPrice: Nullable<number>;
  /** Who made the last write: the owner's session or Ledger's service token. */
  origin?: 'User' | 'Ledger';
  narrativeFalsifiers?: Nullable<string[]>;
}
