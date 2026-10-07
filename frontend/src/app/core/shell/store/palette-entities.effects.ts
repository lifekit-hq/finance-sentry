import {inject} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {pipe, switchMap, tap} from 'rxjs';

import {type PaletteEntities} from '../models/palette-entities.model';
import {PaletteEntitiesService} from '../services/palette-entities.service';

interface StoreMethods {
  setEntities(entities: PaletteEntities): void;
}

export function paletteEntitiesEffects(store: StoreMethods) {
  const service = inject(PaletteEntitiesService);

  const load = rxMethod<void>(
    pipe(
      switchMap(() => service.load()),
      tap(entities => store.setEntities(entities))
    )
  );

  return {load};
}

export function paletteEntitiesHooks(store: ReturnType<typeof paletteEntitiesEffects>) {
  return {
    onInit: () => {
      store.load();
    },
  };
}
