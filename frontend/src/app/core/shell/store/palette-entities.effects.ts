import {computed, inject} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {filter, pipe, switchMap, tap} from 'rxjs';

import {AuthStore} from '../../../modules/auth/store/auth.store';
import {type PaletteEntities} from '../models/palette-entities.model';
import {PaletteEntitiesService} from '../services/palette-entities.service';

interface StoreMethods {
  setEntities(entities: PaletteEntities): void;
}

export function paletteEntitiesEffects(store: StoreMethods) {
  const service = inject(PaletteEntitiesService);

  const load = rxMethod<boolean>(
    pipe(
      filter(Boolean),
      switchMap(() => service.load()),
      tap(entities => store.setEntities(entities))
    )
  );

  return {load};
}

export function paletteEntitiesHooks(store: ReturnType<typeof paletteEntitiesEffects>) {
  const authStore = inject(AuthStore);
  return {
    onInit: () => {
      store.load(computed(() => authStore.sessionResolved() && authStore.isAuthenticated()));
    },
  };
}
