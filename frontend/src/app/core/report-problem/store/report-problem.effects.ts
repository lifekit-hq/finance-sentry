import {HttpErrorResponse, HttpStatusCode} from '@angular/common/http';
import {inject, type Signal} from '@angular/core';
import {Router} from '@angular/router';
import {extractErrorCode} from '@lifekit-hq/core';
import {CmnDrawerRef, CmnShellService, type Nullable, ToastService} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, tap} from 'rxjs';

import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {REPORT_PROBLEM_RATE_LIMITED_CODE} from '../constants/report-problem.constants';
import {ReportProblemService} from '../services/report-problem.service';
import {ClientInfoUtils} from '../utils/client-info.utils';
import {RoutePatternUtils} from '../utils/route-pattern.utils';
import {type ReportProblemState} from './report-problem.state';

interface EffectsStore {
  kind: Signal<ReportProblemState['kind']>;
  setLoading(): void;
  setSuccess(): void;
  setError(errorCode: Nullable<string>): void;
}

export function reportProblemEffects(store: EffectsStore) {
  const service = inject(ReportProblemService);
  const router = inject(Router);
  const shell = inject(CmnShellService);
  const toast = inject(ToastService);
  const drawerRef = inject(CmnDrawerRef);

  /** Sends the typed text (or nothing) with the client part; the sheet stays open with its text on any failure. */
  const submit = rxMethod<Nullable<string>>(
    pipe(
      tap(() => store.setLoading()),
      exhaustMap(text =>
        service
          .submit({
            kind: store.kind(),
            text: text?.trim() || null,
            route: RoutePatternUtils.fromSnapshot(router.routerState.snapshot.root),
            appVersion: APP_VERSION,
            device: ClientInfoUtils.device(shell.isPhone()),
            os: ClientInfoUtils.os(navigator.userAgent),
            browser: ClientInfoUtils.browser(navigator.userAgent),
          })
          .pipe(
            tap(accepted => {
              store.setSuccess();
              toast.success(`Thanks, report ${accepted.reference} sent.`);
              drawerRef.close();
            }),
            catchError((err: unknown) => {
              const bareRateLimit =
                err instanceof HttpErrorResponse &&
                err.status === Number(HttpStatusCode.TooManyRequests);
              store.setError(
                extractErrorCode(err) ?? (bareRateLimit ? REPORT_PROBLEM_RATE_LIMITED_CODE : null)
              );
              return EMPTY;
            })
          )
      )
    )
  );

  return {submit};
}
