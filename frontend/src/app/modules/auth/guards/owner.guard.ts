import {inject} from '@angular/core';
import {type CanActivateFn, Router} from '@angular/router';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {AuthStore} from '../store/auth.store';

export const ownerGuard: CanActivateFn = () => {
  const authStore = inject(AuthStore);
  return authStore.isOwner() ? true : inject(Router).createUrlTree([AppRoute.Dashboard]);
};
