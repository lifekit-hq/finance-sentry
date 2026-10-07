import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {paletteEntitiesComputed} from './palette-entities.computed';
import {paletteEntitiesEffects, paletteEntitiesHooks} from './palette-entities.effects';
import {paletteEntitiesMethods} from './palette-entities.methods';
import {initialPaletteEntitiesState} from './palette-entities.state';

/** Shell-scoped: loaded once per signed-in session, when the shell (and so the palette) mounts. */
export const PaletteEntitiesStore = signalStore(
  withState(initialPaletteEntitiesState),
  withMethods(paletteEntitiesMethods),
  withComputed(paletteEntitiesComputed),
  withMethods(paletteEntitiesEffects),
  withHooks(paletteEntitiesHooks)
);
