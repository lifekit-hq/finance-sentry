import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';
import {PushSubscriptionService} from '@lifekit-hq/core/pwa';

import {type PushDevice, type PushDeviceRow} from '../../models/push/push.model';
import {type PushAction} from './push.state';

interface StateSignals {
  status: Signal<AsyncStatus>;
  errorCode: Signal<Nullable<string>>;
  available: Signal<boolean>;
  pushEnabled: Signal<boolean>;
  devices: Signal<PushDevice[]>;
  thisDeviceId: Signal<Nullable<string>>;
  action: Signal<PushAction>;
  actionErrorCode: Signal<Nullable<string>>;
}

export function pushComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);
  const push = inject(PushSubscriptionService);

  const thisDeviceRegistered = computed(() => store.thisDeviceId() !== null);

  return {
    isLoading: computed(() => store.status() === 'loading' && store.devices().length === 0),
    isBusy: computed(() => store.action() !== 'idle'),
    thisDeviceRegistered,
    rows: computed<PushDeviceRow[]>(() =>
      store.devices().map(device => ({...device, isThisDevice: device.id === store.thisDeviceId()}))
    ),
    /** Why this browser cannot subscribe, or empty when it can (or the server has push off). */
    browserNotice: computed(() => {
      if (!store.available()) {
        return '';
      }
      if (push.requiresInstall()) {
        return 'On iPhone and iPad, add Finance Sentry to your Home Screen first, then turn on notifications from the installed app.';
      }
      if (!push.isSupported()) {
        return 'This browser cannot receive push notifications.';
      }
      if (push.permission() === 'denied' && !thisDeviceRegistered()) {
        return 'Notifications are blocked for this site. Allow them in your browser settings, then try again.';
      }
      return '';
    }),
    serverNotice: computed(() =>
      store.status() === 'idle' && !store.available()
        ? 'Push notifications are not set up on this server yet.'
        : ''
    ),
    /** Turning push off needs nothing from this browser; turning it on needs a server that can send and a browser able to register. */
    canToggle: computed(
      () =>
        store.action() === 'idle' &&
        (store.pushEnabled() ||
          (store.available() &&
            (thisDeviceRegistered() ||
              (push.isSupported() && !push.requiresInstall() && push.permission() !== 'denied'))))
    ),
    loadErrorMessage: computed(() =>
      store.status() === 'error'
        ? (errorMessages.resolve(store.errorCode()) ?? 'Failed to load push settings.')
        : ''
    ),
    actionErrorMessage: computed(() => {
      const code = store.actionErrorCode();
      return code ? (errorMessages.resolve(code) ?? 'Could not update push notifications.') : '';
    }),
  };
}
