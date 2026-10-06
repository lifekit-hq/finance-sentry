import {inject} from '@angular/core';
import {ActivatedRoute} from '@angular/router';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {map, type Observable, pipe, switchMap, tap} from 'rxjs';

import {DateRangeUtils} from '../../../../shared/utils/date-range.utils';
import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {
  type FlowBreakdown,
  type FlowBreakdownRequest,
} from '../../models/flow-breakdown/flow-breakdown.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {MonthKeyUtils} from '../../utils/month-key.utils';

const MONTH_PARAM_PATTERN = /^\d{4}-\d{2}$/;
const DATE_PARAM_PATTERN = /^\d{4}-\d{2}-\d{2}$/;
const MAX_MONTHS = 120;
const DEFAULT_RANGE_MONTHS = 1;

interface EffectsStore {
  setLoading: (request: FlowBreakdownRequest) => void;
  setBreakdown: (breakdown: FlowBreakdown) => void;
  setError: (errorCode: Nullable<string>) => void;
}

export function flowBreakdownEffects(store: EffectsStore) {
  const bankSyncService = inject(BankSyncService);

  return {
    load: rxMethod<FlowBreakdownRequest>(
      pipe(
        tap(request => store.setLoading(request)),
        switchMap(request =>
          (request.kind === 'month'
            ? bankSyncService.getFlowBreakdown(request.month)
            : bankSyncService.getFlowBreakdownRange(request.from, request.to, request.months)
          ).pipe(
            tap(breakdown => store.setBreakdown(breakdown)),
            StoreErrorUtils.catchAndSetError(store)
          )
        )
      )
    ),
  };
}

interface HookStore {
  load: (request$: Observable<FlowBreakdownRequest>) => void;
}

function dateParam(value: Nullable<string>): Nullable<string> {
  return value !== null && DATE_PARAM_PATTERN.test(value) ? value : null;
}

function monthsParam(value: Nullable<string>): number {
  const months = Number(value);
  return Number.isInteger(months) && months >= 1 && months <= MAX_MONTHS
    ? months
    : DEFAULT_RANGE_MONTHS;
}

/**
 * Drives the page off the query params. A `from` and/or `to` date is a dashboard window's day
 * range (with `months` as the history the dashboard loaded around it); otherwise the `month`
 * param, where a missing/invalid one means "now". rxMethod ties the subscription to the
 * store's injector, so it tears down with the page.
 */
export function flowBreakdownHooks(store: HookStore): void {
  const route = inject(ActivatedRoute);
  store.load(
    route.queryParamMap.pipe(
      map((params): FlowBreakdownRequest => {
        const from = dateParam(params.get('from'));
        const to = dateParam(params.get('to'));
        if (from !== null || to !== null) {
          return {
            kind: 'range',
            from,
            to: to ?? DateRangeUtils.toIsoDate(new Date()),
            months: monthsParam(params.get('months')),
          };
        }
        const month = params.get('month');
        return {
          kind: 'month',
          month:
            month !== null && MONTH_PARAM_PATTERN.test(month) ? month : MonthKeyUtils.currentUtc(),
        };
      })
    )
  );
}
