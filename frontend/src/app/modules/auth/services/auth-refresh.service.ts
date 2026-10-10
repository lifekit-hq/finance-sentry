import {HttpErrorResponse, HttpStatusCode} from '@angular/common/http';
import {inject, Injectable, Injector} from '@angular/core';
import {catchError, defer, Observable, share, tap, throwError} from 'rxjs';

import {AuthResponse} from '../models/auth/auth.model';
import {AuthStore} from '../store/auth.store';
import {AuthService} from './auth.service';

/**
 * One in-flight session refresh for the whole app: every 401 that arrives while it runs waits on
 * the same request, so parallel failures cannot rotate the refresh token under each other. The
 * shared stream resets on completion or error, so the next 401 starts a fresh refresh.
 */
@Injectable({providedIn: 'root'})
export class AuthRefreshService {
  private readonly injector = inject(Injector);

  private readonly shared$ = defer(() => {
    // Resolved lazily: AuthStore may still be constructing when the first request is issued.
    const authStore = this.injector.get(AuthStore);
    return this.injector
      .get(AuthService)
      .refresh()
      .pipe(
        tap(res => authStore.applyAuthResponse(res)),
        catchError((err: unknown) => {
          // A 401 from the refresh call itself already expired the session in authInterceptor.
          const alreadyExpired =
            err instanceof HttpErrorResponse &&
            (err.status as HttpStatusCode) === HttpStatusCode.Unauthorized;
          if (!alreadyExpired) {
            authStore.expireSession();
          }
          return throwError(() => err);
        })
      );
  }).pipe(share());

  public refresh(): Observable<AuthResponse> {
    return this.shared$;
  }
}
