import {patchState, type WritableStateSource} from '@ngrx/signals';

import {
  type PushDevice,
  type PushPreferences,
  type PushPublicKey,
} from '../../models/push/push.model';
import {type PushState} from './push.state';

export function pushMethods(store: WritableStateSource<PushState>) {
  return {
    setLoading(): void {
      patchState(store, {status: 'loading', errorCode: null});
    },
    setLoaded(
      key: PushPublicKey,
      devices: PushDevice[],
      preferences: PushPreferences,
      storedDeviceId: Nullable<string>
    ): void {
      patchState(store, {
        status: 'idle',
        errorCode: null,
        available: key.available,
        publicKey: key.publicKey,
        pushEnabled: preferences.pushEnabled,
        devices,
        thisDeviceId: devices.some(d => d.id === storedDeviceId) ? storedDeviceId : null,
      });
    },
    setLoadError(errorCode: Nullable<string>): void {
      patchState(store, {status: 'error', errorCode});
    },
    setAction(
      action: Exclude<PushState['action'], 'idle'>,
      removingId: Nullable<string> = null
    ): void {
      patchState(store, {action, actionErrorCode: null, removingId});
    },
    setActionError(actionErrorCode: Nullable<string>): void {
      patchState(store, {action: 'idle', actionErrorCode, removingId: null});
    },
    setEnabled(device: PushDevice): void {
      patchState(store, state => ({
        action: 'idle' as const,
        actionErrorCode: null,
        pushEnabled: true,
        thisDeviceId: device.id,
        devices: [...state.devices.filter(d => d.id !== device.id), device],
      }));
    },
    setPreference(pushEnabled: boolean): void {
      patchState(store, {action: 'idle', actionErrorCode: null, pushEnabled});
    },
    setRemoved(id: string): void {
      patchState(store, state => ({
        action: 'idle' as const,
        actionErrorCode: null,
        removingId: null,
        devices: state.devices.filter(d => d.id !== id),
        thisDeviceId: state.thisDeviceId === id ? null : state.thisDeviceId,
      }));
    },
  };
}
