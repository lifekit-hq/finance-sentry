import {inject} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
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
  setLoadLoading: () => void;
  setLoadSuccess: () => void;
  setLoadError: (errorCode: Nullable<string>) => void;
  dismissSubscription: (id: string) => void;
  restoreSubscription: (id: string) => void;
  setAddLoading: () => void;
  setAddSuccess: () => void;
  setAddError: (errorCode: Nullable<string>) => void;
  setAddErrorDetail: (errorDetail: Nullable<string>) => void;
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
        store.setLoadSuccess();
      })
    );

  const startAdd = (): void => {
    store.setAddLoading();
    store.setAddErrorDetail(null);
  };

  const reportAddError = (err: unknown): Observable<never> => {
    store.setAddError(extractErrorCode(err));
    store.setAddErrorDetail(ErrorUtils.extractMessage(err));
    return EMPTY;
  };

  const refreshSummary = (): Observable<SubscriptionSummary> =>
    service.getSummary().pipe(tap(summary => store.setSummary(summary)));

  return {
    load: rxMethod<void>(
      pipe(
        tap(() => store.setLoadLoading()),
        switchMap(() =>
          refresh().pipe(
            catchError((err: unknown) => {
              store.setLoadError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
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
        tap(() => startAdd()),
        switchMap(payload =>
          service.add(payload).pipe(
            switchMap(() => refresh()),
            tap(() => store.setAddSuccess()),
            catchError(reportAddError)
          )
        )
      )
    ),
    linkCommitment: rxMethod<{id: string; request: LinkCommitmentRequest}>(
      pipe(
        tap(() => startAdd()),
        switchMap(({id, request}) =>
          service.link(id, request).pipe(
            switchMap(() => refresh()),
            tap(() => store.setAddSuccess()),
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
