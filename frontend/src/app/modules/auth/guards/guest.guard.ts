import {inject} from '@angular/core';
import {CanActivateFn, Router} from '@angular/router';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../store/auth.store';
import {SessionUtils} from '../utils/session.utils';

/** A guest page has no shell to draw, so it waits for the session call to tell it who is here. */
export const guestGuard: CanActivateFn = () => {
  const authStore = inject(AuthStore);
  const router = inject(Router);

  return SessionUtils.afterSession(authStore.sessionResolved, () =>
    authStore.isAuthenticated() ? router.createUrlTree([AppRoute.Dashboard]) : true
  );
};
