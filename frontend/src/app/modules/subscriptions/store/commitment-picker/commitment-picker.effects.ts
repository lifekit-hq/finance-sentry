import {inject, type Signal} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, debounceTime, EMPTY, filter, pipe, switchMap, tap} from 'rxjs';

import {CANDIDATE_SEARCH_DEBOUNCE_MS} from '../../constants/commitment-candidate/commitment-candidate.constants';
import {
  type CommitmentAnchor,
  type CommitmentCandidate,
} from '../../models/commitment-candidate/commitment-candidate.model';
import {CommitmentCandidatesService} from '../../services/commitment-candidates.service';
import {SubscriptionsService} from '../../services/subscriptions.service';

interface EffectsStore {
  search: Signal<string>;
  setSearch: (search: string) => void;
  setLoading: () => void;
  setCandidates: (candidates: CommitmentCandidate[]) => void;
  setError: (errorCode: Nullable<string>) => void;
  setAnchor: (anchor: CommitmentAnchor) => void;
}

export function commitmentPickerEffects(store: EffectsStore) {
  const service = inject(CommitmentCandidatesService);
  const subscriptions = inject(SubscriptionsService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.search(store.search()).pipe(
          tap(page => store.setCandidates(page.items)),
          catchError((err: unknown) => {
            store.setError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  return {
    load,
    loadAnchor: rxMethod<{transactionId: string; onLoaded: (anchor: CommitmentAnchor) => void}>(
      pipe(
        switchMap(({transactionId, onLoaded}) =>
          subscriptions.getCommitmentAnchor(transactionId).pipe(
            tap(anchor => {
              store.setAnchor(anchor);
              onLoaded(anchor);
            }),
            catchError(() => EMPTY)
          )
        )
      )
    ),
    applySearch: rxMethod<string>(
      pipe(
        debounceTime(CANDIDATE_SEARCH_DEBOUNCE_MS),
        filter(search => search.trim() !== store.search().trim()),
        tap(search => store.setSearch(search)),
        tap(() => load())
      )
    ),
  };
}

interface HookStore {
  load: () => void;
}

export function commitmentPickerHooks(store: HookStore): void {
  store.load();
}
