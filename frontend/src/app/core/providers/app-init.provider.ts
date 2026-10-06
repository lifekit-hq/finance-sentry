import {
  type EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
} from '@angular/core';
import {ThemeService} from '@lifekit-hq/ui';
import {catchError, EMPTY, firstValueFrom, tap} from 'rxjs';

import {AuthService} from '../../modules/auth/services/auth.service';
import {AuthStore} from '../../modules/auth/store/auth.store';

export function provideAppInit(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAppInitializer(() => {
      // Instantiated at start so the stored/OS theme and theme-color meta apply on every route, guest pages included.
      inject(ThemeService);
      const authService = inject(AuthService);
      const authStore = inject(AuthStore);
      return firstValueFrom(
        authService.getMe().pipe(
          tap(res => authStore.applyAuthResponse(res)),
          catchError(() => EMPTY)
        ),
        {defaultValue: undefined}
      );
    }),
  ]);
}
