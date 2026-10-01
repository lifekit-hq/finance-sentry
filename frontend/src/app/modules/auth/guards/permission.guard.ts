import {inject} from '@angular/core';
import {type CanMatchFn, Router} from '@angular/router';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {type Permission} from '../../../shared/enums/permission/permission.enum';
import {AuthStore} from '../store/auth.store';

/** Matches the route only for a user holding `permission`; anyone else is sent to the dashboard. */
export function permissionGuard(permission: Permission): CanMatchFn {
  return () =>
    inject(AuthStore).permissions().includes(permission)
      ? true
      : inject(Router).createUrlTree([AppRoute.Dashboard]);
}
