import {type Nullable} from '@lifekit-hq/ui';

import {type VerdictWord} from '../../../../shared/models/scorecard/scorecard.model';

/** Where a name in the universe came from (spec 726 OQ-1). */
export type UniverseSource = 'held' | 'watchlist' | 'curated';

/** An archetype the viewer can pick, with the gates it builds from. */
export interface ArchetypeOption {
  key: string;
  label: string;
  description: string;
  eligibleVerdicts: VerdictWord[];
  minStructure: Nullable<number>;
  minFundamentals: Nullable<number>;
  excludeExtended: boolean;
  ordering: string[];
}

/** One limit the construction applied and the source that set it (spec 726 FR-014). */
export interface AppliedConstraint {
  key: string;
  label: string;
  value: number;
  source: string;
}

export interface ConstructionSleeve {
  assetClass: string;
  weightPct: number;
  builtFromUniverse: boolean;
  note: Nullable<string>;
}

export interface Construction {
  constraints: AppliedConstraint[];
  sleeves: ConstructionSleeve[];
  cashPct: number;
  notes: string[];
  /** Set when the policy cannot be built as written; no weight is then given. */
  refusal: Nullable<string>;
  namesHeld: number;
}

export interface RowVerdict {
  word: VerdictWord;
  rulesVersion: Nullable<number>;
  firedRuleId: Nullable<string>;
  firedRule: Nullable<string>;
}

export interface RowStructure {
  raw: Nullable<number>;
  adjusted: Nullable<number>;
  basis: Nullable<string>;
  notEvaluable: string[];
}

export interface RowFundamentals {
  score: Nullable<number>;
  notEvaluable: string[];
}

export interface RowCrowding {
  class: Nullable<string>;
  extensionFromMa50: Nullable<number>;
}

/** Valuation as raw facts, never a score. */
export interface RowValuation {
  notApplicable: boolean;
  trailingPe: Nullable<number>;
  trailingPeFiveYearAvg: Nullable<number>;
  premiumPct: Nullable<number>;
  impliedUpsidePct: Nullable<number>;
  notEvaluable: Nullable<string>;
  stale: boolean;
}

/**
 * One name: the four axes as facts, the verdict with its rule, and its place in the suggested book. There is
 * no composite score: `constructionRank` is the position in the archetype's ordering of the names it builds.
 */
export interface UniverseRow {
  symbol: string;
  sources: UniverseSource[];
  /** The company's sector and industry from the valuation source; null when it gave none. IPS exclusions match on them. */
  sector: Nullable<string>;
  industry: Nullable<string>;
  failureReason: Nullable<string>;
  scoredAt: Nullable<string>;
  verdict: Nullable<RowVerdict>;
  structure: Nullable<RowStructure>;
  fundamentals: Nullable<RowFundamentals>;
  crowding: Nullable<RowCrowding>;
  valuation: Nullable<RowValuation>;
  constructionRank: Nullable<number>;
  suggestedWeightPct: Nullable<number>;
  reason: string;
}

export interface UniverseScreen {
  archetype: string;
  archetypes: ArchetypeOption[];
  generatedAt: string;
  universeCap: number;
  truncated: boolean;
  construction: Construction;
  rows: UniverseRow[];
}

/** What the screen can be ordered by: each is one raw axis fact or the verdict, never a blend (spec 726 US1). */
export type UniverseSortKey =
  'construction' | 'structure' | 'fundamentals' | 'crowding' | 'valuation' | 'verdict' | 'symbol';

/** The verdict filter: one word, or every name. */
export type UniverseVerdictFilter = VerdictWord | 'All';

/** A row ready to render: the facts as short strings, so the template holds no formatting logic. */
export interface UniverseRowView {
  row: UniverseRow;
  structureText: string;
  /** The raw structure beside the adjusted value the verdict reads; empty when they are the same. */
  structureRawText: string;
  fundamentalsText: string;
  crowdingText: string;
  valuationText: string;
  weightText: string;
  scoredAtText: string;
  verdictRuleText: string;
  notEvaluableTexts: string[];
}
