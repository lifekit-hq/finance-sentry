import {
  HttpErrorResponse,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
  HttpStatusCode,
} from '@angular/common/http';
import {inject, Injector} from '@angular/core';
import {catchError, switchMap, throwError} from 'rxjs';

import {SESSION_PROBE, SKIP_AUTH_REFRESH} from '../constants/auth/auth-http-context.constants';
import {AuthRefreshService} from '../services/auth-refresh.service';
import {AuthStore} from '../store/auth.store';

function isUnauthorizedError(err: unknown): boolean {
  return (
    err instanceof HttpErrorResponse &&
    (err.status as HttpStatusCode) === HttpStatusCode.Unauthorized
  );
}

export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
) => {
  const injector = inject(Injector);
  const authReq = req.clone({withCredentials: true});

  return next(authReq).pipe(
    catchError((err: unknown) => {
      const isUnauthorized = isUnauthorizedError(err);
      const skipRefresh = req.context.get(SKIP_AUTH_REFRESH);

      if (isUnauthorized && !skipRefresh) {
        return injector
          .get(AuthRefreshService)
          .refresh()
          .pipe(
            // The shared refresh has already ended the session if it failed.
            catchError(() => throwError(() => err)),
            switchMap(() =>
              next(authReq).pipe(
                catchError((retryErr: unknown) => {
                  if (isUnauthorizedError(retryErr)) {
                    injector.get(AuthStore).expireSession();
                  }
                  return throwError(() => retryErr);
                })
              )
            )
          );
      }

      // An anonymous visitor's session-restore probe is expected to 401; logging out would
      // navigate to /login and clobber guest routes such as /accept-invite.
      if (isUnauthorized && skipRefresh && !req.context.get(SESSION_PROBE)) {
        injector.get(AuthStore).expireSession();
      }

      return throwError(() => err);
    })
  );
};
