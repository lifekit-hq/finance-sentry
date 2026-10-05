import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, forkJoin, type Observable, pipe, switchMap, tap} from 'rxjs';

import {ErrorUtils} from '../../../../shared/utils/error.utils';
import {
  type AddCommitmentRequest,
  type LinkCommitmentRequest,
  type Subscription,
  type SubscriptionSummary,
} from '../../models/subscription/subscription.model';
import {SubscriptionsService} from '../../services/subscriptions.service';

interface EffectsStore {
  setData: (subscriptions: Subscription[], hasInsufficientHistory: boolean) => void;
  setSummary: (summary: SubscriptionSummary) => void;
  dismissSubscription: (id: string) => void;
  restoreSubscription: (id: string) => void;
  setAddError: (errorCode: Nullable<string>, errorDetail?: Nullable<string>) => void;
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

  const reportAddError = (err: unknown): Observable<never> => {
    store.setAddError(extractErrorCode(err), ErrorUtils.extractMessage(err));
    return EMPTY;
  };

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
    addCommitment: rxMethod<AddCommitmentRequest>(
      pipe(
        tap(() => store.setAddError(null)),
        switchMap(payload =>
          service.add(payload).pipe(
            switchMap(() => refresh()),
            catchError(reportAddError)
          )
        )
      )
    ),
    linkCommitment: rxMethod<{id: string; request: LinkCommitmentRequest}>(
      pipe(
        tap(() => store.setAddError(null)),
        switchMap(({id, request}) =>
          service.link(id, request).pipe(
            switchMap(() => refresh()),
            catchError(reportAddError)
          )
        )
      )
    ),
  };
}

interface HookStore extends EffectsStore {
  load: () => void;
}

export function subscriptionsHooks(store: HookStore): void {
  store.load();
}
