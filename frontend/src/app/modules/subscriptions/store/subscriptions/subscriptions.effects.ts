import {inject} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {forkJoin, type Observable, pipe, switchMap, tap} from 'rxjs';

import {
  type AddInstallmentRequest,
  type AddSubscriptionRequest,
  type Subscription,
  type SubscriptionSummary,
} from '../../models/subscription/subscription.model';
import {SubscriptionsService} from '../../services/subscriptions.service';

interface EffectsStore {
  setData: (subscriptions: Subscription[], hasInsufficientHistory: boolean) => void;
  setSummary: (summary: SubscriptionSummary) => void;
  dismissSubscription: (id: string) => void;
  restoreSubscription: (id: string) => void;
}

export function subscriptionsEffects(store: EffectsStore) {
  const service = inject(SubscriptionsService);

  const refresh = (): Observable<unknown> =>
    forkJoin({
      list$: service.getSubscriptions(true),
      summary$: service.getSummary(),
    }).pipe(
      tap(({list$, summary$}) => {
        store.setData(list$.items, list$.hasInsufficientHistory);
        store.setSummary(summary$);
      })
    );

  const refreshSummary = (): Observable<SubscriptionSummary> =>
    service.getSummary().pipe(tap(summary => store.setSummary(summary)));

  return {
    load: rxMethod<void>(pipe(switchMap(() => refresh()))),
    dismiss: rxMethod<string>(
      pipe(
        switchMap(id =>
          service.dismiss(id).pipe(
            tap(() => store.dismissSubscription(id)),
            switchMap(() => refreshSummary())
          )
        )
      )
    ),
    restore: rxMethod<string>(
      pipe(
        switchMap(id =>
          service.restore(id).pipe(
            tap(() => store.restoreSubscription(id)),
            switchMap(() => refreshSummary())
          )
        )
      )
    ),
    setInstallmentTerm: rxMethod<{id: string; termCount: Nullable<number>}>(
      pipe(
        switchMap(({id, termCount}) =>
          service.setInstallmentTerm(id, termCount).pipe(switchMap(() => refresh()))
        )
      )
    ),
    completeInstallment: rxMethod<string>(
      pipe(switchMap(id => service.completeInstallment(id).pipe(switchMap(() => refresh()))))
    ),
    deleteInstallment: rxMethod<string>(
      pipe(switchMap(id => service.deleteInstallment(id).pipe(switchMap(() => refresh()))))
    ),
    addInstallment: rxMethod<AddInstallmentRequest>(
      pipe(switchMap(payload => service.addInstallment(payload).pipe(switchMap(() => refresh()))))
    ),
    addSubscription: rxMethod<AddSubscriptionRequest>(
      pipe(switchMap(payload => service.addSubscription(payload).pipe(switchMap(() => refresh()))))
    ),
  };
}

interface HookStore extends EffectsStore {
  load: () => void;
}

export function subscriptionsHooks(store: HookStore): void {
  store.load();
}
