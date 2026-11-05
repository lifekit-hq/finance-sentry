import {computed, type Signal} from '@angular/core';
import {type Nullable, type SegmentedOption} from '@lifekit-hq/ui';

import {
  type UniverseRowView,
  type UniverseScreen,
  type UniverseSortKey,
  type UniverseVerdictFilter,
} from '../../models/universe-screen/universe-screen.model';
import {UniverseScreenUtils} from '../../utils/universe-screen.utils';

interface StateSignals {
  screen: Signal<Nullable<UniverseScreen>>;
  sortKey: Signal<UniverseSortKey>;
  verdictFilter: Signal<UniverseVerdictFilter>;
}

export function universeScreenComputed(store: StateSignals) {
  return {
    /** The archetypes as segments; the screen names the archetype it built, so the choice always matches the rows. */
    archetypeSegments: computed((): SegmentedOption[] =>
      (store.screen()?.archetypes ?? []).map(a => ({value: a.key, label: a.label}))
    ),
    /** The archetype the rows were built for, with the gates it builds from. */
    activeArchetype: computed(() => {
      const screen = store.screen();
      return screen?.archetypes.find(a => a.key === screen.archetype) ?? null;
    }),
    hasRows: computed((): boolean => (store.screen()?.rows.length ?? 0) > 0),
    /** The rows after the verdict filter and the sort, each as display strings. */
    rows: computed((): UniverseRowView[] => {
      const rows = store.screen()?.rows ?? [];
      const filtered = UniverseScreenUtils.filter(rows, store.verdictFilter());
      return UniverseScreenUtils.sort(filtered, store.sortKey()).map(r =>
        UniverseScreenUtils.toView(r)
      );
    }),
  };
}
