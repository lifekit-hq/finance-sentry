import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type PaletteEntities} from '../models/palette-entities.model';
import {type PaletteEntitiesState} from './palette-entities.state';

export function paletteEntitiesMethods(store: WritableStateSource<PaletteEntitiesState>) {
  return {
    setEntities(entities: PaletteEntities): void {
      patchState(store, {entities});
    },
  };
}
