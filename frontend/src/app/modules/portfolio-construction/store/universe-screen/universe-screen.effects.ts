import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {type GapMode} from '../../models/gap-mode/gap-mode.model';
import {type UniverseScreen} from '../../models/universe-screen/universe-screen.model';
import {PortfolioConstructionService} from '../../services/portfolio-construction.service';

interface EffectsStore {
  archetype: () => Nullable<string>;
  screen: () => Nullable<UniverseScreen>;
  setScreen: (screen: UniverseScreen) => void;
  setGap: (gap: Nullable<GapMode>) => void;
  setGapLoading: () => void;
  setGapSuccess: () => void;
  setGapError: (errorCode: Nullable<string>) => void;
  setArchetype: (archetype: Nullable<string>) => void;
  setLoadLoading: () => void;
  setLoadSuccess: () => void;
  setLoadError: (errorCode: Nullable<string>) => void;
}

export function universeScreenEffects(store: EffectsStore) {
  const service = inject(PortfolioConstructionService);

  /** Loads how the owner's book compares with the screen's target; a failure never blanks the screen. */
  const loadGap = rxMethod<void>(
    pipe(
      tap(() => store.setGapLoading()),
      switchMap(() =>
        service.getGap(store.screen()?.archetype ?? undefined).pipe(
          tap(gap => {
            store.setGap(gap);
            store.setGapSuccess();
          }),
          catchError((err: unknown) => {
            store.setGapError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  return {
    loadGap,
    /** Loads the screen, then the gap for the archetype it built; a newer request replaces one still in flight. */
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoadLoading()),
        switchMap(() =>
          service.getUniverse(store.archetype() ?? undefined).pipe(
            tap(screen => {
              store.setScreen(screen);
              store.setLoadSuccess();
              loadGap();
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
