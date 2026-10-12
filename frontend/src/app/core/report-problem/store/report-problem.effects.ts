import {inject, type Signal} from '@angular/core';
import {Router} from '@angular/router';
import {extractErrorCode} from '@lifekit-hq/core';
import {CmnDrawerRef, CmnShellService, ToastService} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, pipe, tap} from 'rxjs';

import {APP_VERSION} from '../../../shared/constants/version/version.constants';
import {type ReportProblemStatus} from '../models/report-problem.model';
import {ReportProblemService} from '../services/report-problem.service';
import {ClientInfoUtils} from '../utils/client-info.utils';
import {RoutePatternUtils} from '../utils/route-pattern.utils';
import {type ReportProblemState} from './report-problem.state';

interface EffectsStore {
  kind: Signal<ReportProblemState['kind']>;
  setStatus(status: ReportProblemStatus): void;
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
      tap(() => store.setStatus('submitting')),
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
              store.setStatus('idle');
              toast.success(`Thanks, report ${accepted.reference} sent.`);
              drawerRef.close();
            }),
            catchError((err: unknown) => {
              store.setError(extractErrorCode(err));
              return EMPTY;
            })
          )
      )
    )
  );

  return {submit};
}
