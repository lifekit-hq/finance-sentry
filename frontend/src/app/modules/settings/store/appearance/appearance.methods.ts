import {type ThemeSeed} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type AppearanceState} from './appearance.state';

export function appearanceMethods(store: WritableStateSource<AppearanceState>) {
  return {
    /** Mirrors the colour `ThemeService` applies; a reset keeps the last intensity on the slider. */
    setApplied(applied: Nullable<ThemeSeed>): void {
      patchState(store, state => ({
        seed: applied?.seed ?? null,
        intensity: applied?.intensity ?? state.intensity,
      }));
    },
  };
}
