import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type UniverseScreen} from '../../models/universe-screen/universe-screen.model';
import {PortfolioConstructionService} from '../../services/portfolio-construction.service';

interface EffectsStore {
  archetype: () => Nullable<string>;
  setScreen: (screen: UniverseScreen) => void;
  setArchetype: (archetype: Nullable<string>) => void;
  setLoadLoading: () => void;
  setLoadSuccess: () => void;
  setLoadError: (errorCode: Nullable<string>) => void;
}

export function universeScreenEffects(store: EffectsStore) {
  const service = inject(PortfolioConstructionService);

  return {
    /** Loads the screen; a newer request replaces one still in flight. */
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoadLoading()),
        switchMap(() =>
          service.getUniverse(store.archetype() ?? undefined).pipe(
            tap(screen => {
              store.setScreen(screen);
              store.setLoadSuccess();
            }),
            catchError((err: unknown) => {
              store.setLoadError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
  };
}

interface HookStore extends EffectsStore {
  load: () => void;
}

export function universeScreenHooks(store: HookStore): void {
  store.load();
}
