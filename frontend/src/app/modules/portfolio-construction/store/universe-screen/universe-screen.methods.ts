import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type GapMode} from '../../models/gap-mode/gap-mode.model';
import {
  type UniverseScreen,
  type UniverseSortKey,
  type UniverseVerdictFilter,
} from '../../models/universe-screen/universe-screen.model';
import {type UniverseScreenState} from './universe-screen.state';

export function universeScreenMethods(store: WritableStateSource<UniverseScreenState>) {
  return {
    setScreen(screen: UniverseScreen): void {
      patchState(store, {screen});
    },
    setGap(gap: Nullable<GapMode>): void {
      patchState(store, {gap});
    },
    setArchetype(archetype: Nullable<string>): void {
      patchState(store, {archetype});
    },
    setSortKey(sortKey: UniverseSortKey): void {
      patchState(store, {sortKey});
    },
    setVerdictFilter(verdictFilter: UniverseVerdictFilter): void {
      patchState(store, {verdictFilter});
    },
  };
}
