import {inject} from '@angular/core';
import {type CanActivateFn, Router} from '@angular/router';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../store/auth.store';

/**
 * While the first session call is out the shell must still draw, so the guard lets the navigation
 * through and records its URL; the app initializer sends a signed-out visitor to sign in once the
 * call answers.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  if (authStore.isAuthenticated()) {
    return true;
  }

  if (!authStore.sessionResolved()) {
    authStore.deferAuthUrl(state.url);
    return true;
  }

  return router.createUrlTree([AppRoute.Login], {
    queryParams: {returnUrl: state.url},
  });
};
