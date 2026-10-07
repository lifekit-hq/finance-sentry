import {
  HttpErrorResponse,
  HttpHandlerFn,
  HttpInterceptorFn,
  HttpRequest,
  HttpStatusCode,
} from '@angular/common/http';
import {inject, Injector} from '@angular/core';
import {catchError, switchMap, throwError} from 'rxjs';

import {AuthService} from '../services/auth.service';
import {AuthStore} from '../store/auth.store';

function isAuthEndpoint(req: HttpRequest<unknown>): boolean {
  return req.url.includes('/auth/');
}

function isSessionProbe(req: HttpRequest<unknown>): boolean {
  return req.method === 'GET' && req.url.endsWith('/auth/me');
}

export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
) => {
  const injector = inject(Injector);
  const authReq = req.clone({withCredentials: true});

  return next(authReq).pipe(
    catchError((err: unknown) => {
      const authStore = injector.get(AuthStore);
      const authService = injector.get(AuthService);
      const isUnauthorized =
        err instanceof HttpErrorResponse &&
        (err.status as HttpStatusCode) === HttpStatusCode.Unauthorized;

      if (isUnauthorized && !isAuthEndpoint(req)) {
        return authService.refresh().pipe(
          switchMap(res => {
            authStore.applyAuthResponse(res);
            return next(authReq);
          }),
          catchError(() => {
            authStore.expireSession();
            return throwError(() => err);
          })
        );
      }

      // An anonymous visitor's session-restore probe is expected to 401; logging out would
      // navigate to /login and clobber guest routes such as /accept-invite.
      if (isUnauthorized && isAuthEndpoint(req) && !isSessionProbe(req)) {
        authStore.expireSession();
      }

      return throwError(() => err);
    })
  );
};
