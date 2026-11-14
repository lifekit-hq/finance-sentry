/* eslint-disable @typescript-eslint/naming-convention -- map keys are the API's PascalCase enum values */
import {type SelectOption} from '@lifekit-hq/ui';

import {
  type UniverseSortKey,
  type UniverseSource,
  type UniverseVerdictFilter,
} from '../../models/universe-screen/universe-screen.model';

export const UNIVERSE_SORT_OPTIONS: readonly (SelectOption & {value: UniverseSortKey})[] = [
  {label: 'Suggested book', value: 'construction'},
  {label: 'Structure (raw)', value: 'structure'},
  {label: 'Fundamentals', value: 'fundamentals'},
  {label: 'Crowding', value: 'crowding'},
  {label: 'P/E premium', value: 'valuation'},
  {label: 'Verdict', value: 'verdict'},
  {label: 'Ticker', value: 'symbol'},
];

export const UNIVERSE_DEFAULT_SORT: UniverseSortKey = 'construction';
export const UNIVERSE_ALL_FILTER: UniverseVerdictFilter = 'All';
/** The word a row with no verdict at all (it failed to score) reads as. */
export const UNIVERSE_NOT_SCORED_WORD: UniverseVerdictFilter = 'NotScored';

export const UNIVERSE_VERDICT_FILTERS: readonly {value: UniverseVerdictFilter; label: string}[] = [
  {value: 'All', label: 'All'},
  {value: 'StrongBuy', label: 'STRONG BUY'},
  {value: 'Buy', label: 'BUY'},
  {value: 'Wait', label: 'WAIT'},
  {value: 'Avoid', label: 'AVOID'},
  {value: 'NotScored', label: 'NOT SCORED'},
];

/** Verdict order for the verdict sort: strongest first. */
export const UNIVERSE_VERDICT_ORDER: Readonly<Record<string, number>> = {
  StrongBuy: 0,
  Buy: 1,
  Wait: 2,
  Avoid: 3,
  NotScored: 4,
};

/** Crowding order for the crowding sort: earliest first. */
export const UNIVERSE_CROWDING_ORDER: Readonly<Record<string, number>> = {
  Early: 0,
  Normal: 1,
  Extended: 2,
};

export const UNIVERSE_SOURCE_LABEL: Readonly<Record<UniverseSource, string>> = {
  held: 'Held',
  watchlist: 'Watchlist',
  coverage: 'Coverage',
};

export const UNIVERSE_TITLE_NOTE =
  'Every name in the universe with its four axes and a one-word verdict, and the book each archetype would hold. ' +
  'Nothing is bought or sold here.';

export const UNIVERSE_WEIGHT_NOTE =
  'Suggested weights are equal across the names the archetype builds, each capped at the stricter of its ' +
  'default and your risk rules. The rest is cash.';

export const UNIVERSE_TRUNCATED_NOTE =
  'The universe is capped, so some coverage names are not scored.';

export const UNIVERSE_EMPTY_COPY = {
  message: 'No names to score yet',
  subMessage: 'Add a name to your watchlist, or connect an account that holds shares.',
  icon: 'Layers',
} as const;

export const UNIVERSE_NO_MATCH_COPY = {
  message: 'No names match this filter',
  subMessage: 'Choose another verdict.',
  icon: 'Filter',
} as const;

export const UNIVERSE_MISSING = 'no data';
export const UNIVERSE_SKELETON_ROWS = 6;
export const UNIVERSE_ARCHETYPE_LABEL = 'Archetype';
export const UNIVERSE_SORT_LABEL = 'Order by';
export const UNIVERSE_FILTER_LABEL = 'Verdict';
export const UNIVERSE_WHY_LABEL = 'Why';
