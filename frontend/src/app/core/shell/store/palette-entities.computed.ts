import {computed, type Signal} from '@angular/core';

import {PaletteEntityUtils} from '../utils/palette-entity.utils';
import {type PaletteEntitiesState} from './palette-entities.state';

export function paletteEntitiesComputed(store: {
  entities: Signal<PaletteEntitiesState['entities']>;
}) {
  return {
    items: computed(() => PaletteEntityUtils.items(store.entities())),
  };
}
