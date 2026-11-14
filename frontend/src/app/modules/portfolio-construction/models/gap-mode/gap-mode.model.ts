import {type Nullable} from '@lifekit-hq/ui';

import {type VerdictWord} from '../../../../shared/models/scorecard/scorecard.model';

/** Where a held figure stands against its target (spec 726 US4). */
export type GapStatus = 'Missing' | 'Underweight' | 'OnTarget' | 'Overweight';

/** The reading of the gap and the names behind it, largest gap first. */
export interface GapSummary {
  headline: string;
  missingNames: string[];
  underweightNames: string[];
  overweightNames: string[];
  onTargetCount: number;
}

/** One figure held against its target. A gap is target minus current: positive is a shortfall, negative an excess. */
export interface GapFigures {
  status: GapStatus;
  currentPct: number;
  targetPct: number;
  gapPct: number;
  currentUsd: number;
  /** Null when the book has no value to size against. */
  targetUsd: Nullable<number>;
  gapUsd: Nullable<number>;
}

export interface SleeveGap extends GapFigures {
  assetClass: string;
}

export interface SectorGap extends GapFigures {
  sector: string;
  symbols: string[];
}

export interface NameGap extends GapFigures {
  symbol: string;
  sector: Nullable<string>;
  verdict: Nullable<VerdictWord>;
  /** An Overweight name the target gives no weight at all. */
  heldOutsideTarget: boolean;
  reason: string;
}

/** Gap mode: the book held against the constructed target for an archetype. Informational, never an order. */
export interface GapMode {
  archetype: string;
  generatedAt: string;
  /** The informational label shown with the figures. */
  label: string;
  hasBook: boolean;
  bookValueUsd: number;
  bookStale: boolean;
  staleSources: string[];
  /** Set when the policy cannot be built as written; no gap is then given. */
  refusal: Nullable<string>;
  summary: GapSummary;
  sleeves: SleeveGap[];
  sectors: SectorGap[];
  names: NameGap[];
  notes: string[];
}
