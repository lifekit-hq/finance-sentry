import {
  type EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import {Router} from '@angular/router';
import {ThemeService} from '@lifekit-hq/ui';
import {catchError, EMPTY, finalize, tap} from 'rxjs';

import {AuthService} from '../../modules/auth/services/auth.service';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';

export function provideAppInit(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      // Instantiated at start so the stored/OS theme and theme-color meta apply on every route, guest pages included.
      inject(ThemeService);
      const authService = inject(AuthService);
      const authStore = inject(AuthStore);
      const router = inject(Router);
      // The call is not awaited: bootstrap goes on, so the shell draws while it is out and only the
      // routed content waits on `sessionResolved`.
      authService
        .getMe()
        .pipe(
          tap(res => authStore.applyAuthResponse(res)),
          catchError(() => EMPTY),
          finalize(() => {
            const deferredUrl = authStore.deferredAuthUrl();
            authStore.resolveSession();
            if (deferredUrl !== null && !authStore.isAuthenticated()) {
              void router.navigate([AppRoute.Login], {queryParams: {returnUrl: deferredUrl}});
            }
          })
        )
        .subscribe();
    }),
  ]);
}
