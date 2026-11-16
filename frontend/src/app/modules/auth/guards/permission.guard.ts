import {inject} from '@angular/core';
import {type CanMatchFn, Router} from '@angular/router';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {type Permission} from '../../../shared/enums/permission/permission.enum';
import {AuthStore} from '../store/auth.store';
import {SessionUtils} from '../utils/session.utils';

/** Matches the route only for a user holding `permission`; anyone else is sent to the dashboard. */
export function permissionGuard(permission: Permission): CanMatchFn {
  return () => {
    const authStore = inject(AuthStore);
    const router = inject(Router);

    return SessionUtils.afterSession(authStore.sessionResolved, () =>
      authStore.permissions().includes(permission)
        ? true
        : router.createUrlTree([AppRoute.Dashboard])
    );
  };
}
