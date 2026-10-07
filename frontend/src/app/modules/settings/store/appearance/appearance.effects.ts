import {inject} from '@angular/core';
import {type ThemeSeed, ThemeService} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {type Observable, pipe, tap} from 'rxjs';

import {type ThemeChoice} from '../../models/appearance/appearance.model';

interface EffectsStore {
  setApplied: (applied: Nullable<ThemeSeed>) => void;
}

export function appearanceEffects(store: EffectsStore) {
  const themeService = inject(ThemeService);

  return {
    // The service persists the colour per device and the pre-paint script re-applies it on the
    // next load; `activeSeed$` then feeds the result back into the store.
    choose(choice: ThemeChoice): void {
      if (choice.seed === null) {
        themeService.resetSeed();
        return;
      }
      themeService.setSeed(choice.seed, choice.intensity);
    },
    follow: rxMethod<Nullable<ThemeSeed>>(pipe(tap(applied => store.setApplied(applied)))),
  };
}

interface HookStore {
  follow: (applied: Observable<Nullable<ThemeSeed>>) => unknown;
}

export function appearanceHooks(store: HookStore): void {
  store.follow(inject(ThemeService).activeSeed$);
}
