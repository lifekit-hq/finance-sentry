import {inject} from '@angular/core';
import {type CanMatchFn, Router} from '@angular/router';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {type Permission} from '../../../shared/enums/permission/permission.enum';
import {AuthStore} from '../store/auth.store';
import {SessionUtils} from '../utils/session.utils';

/**
 * Matches the route only for a user holding `permission`; anyone else is sent to the dashboard.
 * While the first session call is out the route matches so the shell can draw; a signed-in user
 * who turns out to lack the permission is sent to the dashboard once the call answers (a
 * signed-out visitor is left to the sign-in redirect).
 */
export function permissionGuard(permission: Permission): CanMatchFn {
  return () => {
    const authStore = inject(AuthStore);
    const router = inject(Router);
    const holdsPermission = () => authStore.permissions().includes(permission);

    if (authStore.sessionResolved()) {
      return holdsPermission() ? true : router.createUrlTree([AppRoute.Dashboard]);
    }

    SessionUtils.afterSession(authStore.sessionResolved, () => {
      if (authStore.isAuthenticated() && !holdsPermission()) {
        void router.navigateByUrl(router.createUrlTree([AppRoute.Dashboard]));
      }
    }).subscribe();
    return true;
  };
}
