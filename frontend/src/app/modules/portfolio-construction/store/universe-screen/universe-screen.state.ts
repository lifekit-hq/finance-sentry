import {type Nullable} from '@lifekit-hq/ui';

import {
  UNIVERSE_ALL_FILTER,
  UNIVERSE_DEFAULT_SORT,
} from '../../constants/universe-screen/universe-screen.constants';
import {
  type UniverseScreen,
  type UniverseSortKey,
  type UniverseVerdictFilter,
} from '../../models/universe-screen/universe-screen.model';

export interface UniverseScreenState {
  screen: Nullable<UniverseScreen>;
  /** The archetype asked for; null asks for the configured default. */
  archetype: Nullable<string>;
  sortKey: UniverseSortKey;
  verdictFilter: UniverseVerdictFilter;
}

export const initialUniverseScreenState: UniverseScreenState = {
  screen: null,
  archetype: null,
  sortKey: UNIVERSE_DEFAULT_SORT,
  verdictFilter: UNIVERSE_ALL_FILTER,
};
