import {inject, type Signal} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, filter, pipe, switchMap, tap} from 'rxjs';

import {type FxImpact} from '../../models/fx-impact/fx-impact.model';
import {type Subscription} from '../../models/subscription/subscription.model';
import {SubscriptionsService} from '../../services/subscriptions.service';
import {FxImpactUtils} from '../../utils/fx-impact.utils';
import {SubscriptionsStore} from '../subscriptions/subscriptions.store';

interface EffectsStore {
  setImpact: (impact: FxImpact) => void;
  clearImpact: () => void;
  setEnabled: (enabled: boolean) => void;
}

export function fxImpactEffects(store: EffectsStore) {
  const service = inject(SubscriptionsService);

  return {
    // Re-reads whenever the page's list changes (a term set, a plan dismissed), so the remaining payments
    // never go stale; an empty list is the page before its load lands, nothing to price yet.
    load: rxMethod<Subscription[]>(
      pipe(
        filter(subscriptions => subscriptions.length > 0),
        switchMap(() =>
          service.getFxImpact().pipe(
            tap(impact => store.setImpact(impact)),
            // Supplementary: a failed read hides the widget instead of putting an error on the page.
            catchError(() => {
              store.clearImpact();
              return EMPTY;
            })
          )
        )
      )
    ),
    toggleEnabled(enabled: boolean): void {
      FxImpactUtils.writeEnabled(enabled);
      store.setEnabled(enabled);
    },
  };
}

interface HookStore extends EffectsStore {
  load: (subscriptions: Signal<Subscription[]>) => void;
}

export function fxImpactHooks(store: HookStore): void {
  store.setEnabled(FxImpactUtils.readEnabled());
  store.load(inject(SubscriptionsStore).subscriptions);
}
