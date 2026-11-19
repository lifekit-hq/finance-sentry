import {inject} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type BookPerformance} from '../../models/book-performance/book-performance.model';
import {type TrackRecord} from '../../models/book-performance/track-record.model';
import {BookPerformanceService} from '../../services/book-performance.service';
import {TrackRecordService} from '../../services/track-record.service';

interface StoreMethods {
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
  setPerformance(performance: BookPerformance): void;
  setTrackRecord(trackRecord: TrackRecord): void;
}

export function bookPerformanceEffects(store: StoreMethods) {
  const service = inject(BookPerformanceService);
  const trackRecords = inject(TrackRecordService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        service.getPerformance().pipe(
          tap(performance => {
            store.setPerformance(performance);
            store.setSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setError(code),
          })
        )
      )
    )
  );

  // The sustained-underperformance flag is an extra on the card: when the stored record cannot be read the
  // figures stay, only the flag is left off.
  const loadTrackRecord = rxMethod<void>(
    pipe(
      switchMap(() =>
        trackRecords.getTrackRecord().pipe(
          tap(record => {
            store.setTrackRecord(record);
          }),
          catchError(() => EMPTY)
        )
      )
    )
  );

  return {load, loadTrackRecord};
}

export function bookPerformanceHooks(store: ReturnType<typeof bookPerformanceEffects>) {
  return {
    onInit: () => {
      store.load();
      store.loadTrackRecord();
    },
  };
}
