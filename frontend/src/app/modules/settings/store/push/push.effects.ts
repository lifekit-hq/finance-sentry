import {inject, type Signal} from '@angular/core';
import {extractErrorCode} from '@lifekit-hq/core';
import {PushSubscriptionService} from '@lifekit-hq/core/pwa';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, EMPTY, exhaustMap, forkJoin, from, map, pipe, switchMap, tap} from 'rxjs';

import {PUSH_PERMISSION_DENIED, PUSH_SUBSCRIBE_FAILED} from '../../constants/push/push.constants';
import {
  type PushDevice,
  type PushPreferences,
  type PushPublicKey,
} from '../../models/push/push.model';
import {PushNotificationsService} from '../../services/push-notifications.service';
import {PushDeviceUtils} from '../../utils/push-device.utils';
import {type PushAction} from './push.state';

interface EffectsStore {
  publicKey: Signal<Nullable<string>>;
  thisDeviceId: Signal<Nullable<string>>;
  setLoading: () => void;
  setLoaded: (
    key: PushPublicKey,
    devices: PushDevice[],
    preferences: PushPreferences,
    storedDeviceId: Nullable<string>
  ) => void;
  setLoadError: (errorCode: Nullable<string>) => void;
  setAction: (action: Exclude<PushAction, 'idle'>, removingId?: Nullable<string>) => void;
  setActionError: (errorCode: Nullable<string>) => void;
  setEnabled: (device: PushDevice) => void;
  setPreference: (pushEnabled: boolean) => void;
  setRemoved: (id: string) => void;
}

export function pushEffects(store: EffectsStore) {
  const service = inject(PushNotificationsService);
  const push = inject(PushSubscriptionService);

  const load = rxMethod<void>(
    pipe(
      tap(() => store.setLoading()),
      switchMap(() =>
        forkJoin([service.getPublicKey(), service.listDevices(), service.getPreferences()]).pipe(
          tap(([key, devices, preferences]) =>
            store.setLoaded(key, devices, preferences, PushDeviceUtils.readId())
          ),
          catchError((err: unknown) => {
            store.setLoadError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  /** Subscribes this browser, registers it and turns push on. Must run from a user gesture (iOS). */
  const enable = rxMethod<void>(
    pipe(
      tap(() => store.setAction('enabling')),
      exhaustMap(() => {
        const key = store.publicKey();
        if (!key) {
          store.setActionError('PUSH_UNAVAILABLE');
          return EMPTY;
        }
        return from(push.subscribe(key)).pipe(
          map(json => PushDeviceUtils.toRequest(json)),
          switchMap(request => service.register(request)),
          switchMap(device => service.setPreferences(true).pipe(map(() => device))),
          tap(device => {
            PushDeviceUtils.writeId(device.id);
            store.setEnabled(device);
          }),
          catchError((err: unknown) => {
            void push.unsubscribe().catch(() => undefined);
            store.setActionError(
              extractErrorCode(err) ??
                (push.permission() === 'denied' ? PUSH_PERMISSION_DENIED : PUSH_SUBSCRIBE_FAILED)
            );
            return EMPTY;
          })
        );
      })
    )
  );

  const savePreference = rxMethod<boolean>(
    pipe(
      tap(() => store.setAction('saving')),
      exhaustMap(pushEnabled =>
        service.setPreferences(pushEnabled).pipe(
          tap(preferences => store.setPreference(preferences.pushEnabled)),
          catchError((err: unknown) => {
            store.setActionError(extractErrorCode(err));
            return EMPTY;
          })
        )
      )
    )
  );

  return {
    load,
    enable,
    savePreference,
    /** The toggle: turning on registers this browser first when it has no device yet. */
    togglePush(checked: boolean): void {
      if (checked && store.thisDeviceId() === null) {
        enable();
        return;
      }
      savePreference(checked);
    },
    remove: rxMethod<string>(
      pipe(
        tap(id => store.setAction('removing', id)),
        exhaustMap(id =>
          service.remove(id).pipe(
            switchMap(() => {
              if (id !== store.thisDeviceId()) {
                return from([undefined]);
              }
              PushDeviceUtils.clearId();
              return from(push.unsubscribe());
            }),
            tap(() => store.setRemoved(id)),
            catchError((err: unknown) => {
              store.setActionError(extractErrorCode(err));
              return EMPTY;
            })
          )
        )
      )
    ),
  };
}

interface HookStore {
  load: () => void;
}

export function pushHooks(store: HookStore): void {
  store.load();
}
