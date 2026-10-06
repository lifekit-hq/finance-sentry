import {inject, Injectable, Injector} from '@angular/core';
import {PushSubscriptionService} from '@lifekit-hq/core/pwa';
import {catchError, defer, EMPTY, from, type Observable, of, switchMap, tap} from 'rxjs';

import {PushDeviceUtils} from '../utils/push-device.utils';
import {PushNotificationsService} from './push-notifications.service';

/** Detaches this browser from the signed-in account, so the next person to sign in here is not sent the previous one's alerts. */
@Injectable({providedIn: 'root'})
export class PushSessionService {
  private readonly injector = inject(Injector);

  /** Call before the session is cleared: the delete needs the access token. Never errors. */
  public release(): Observable<void> {
    // Resolved lazily: the push service needs the service worker, which only exists on a real push-capable page.
    return defer(() => {
      const api = this.injector.get(PushNotificationsService);
      const push = this.injector.get(PushSubscriptionService);
      const id = PushDeviceUtils.readId();
      const remove$ = id === null ? of(undefined) : api.remove(id);
      return remove$.pipe(
        switchMap(() => from(push.unsubscribe())),
        tap(() => PushDeviceUtils.clearId())
      );
    }).pipe(catchError(() => EMPTY));
  }
}
