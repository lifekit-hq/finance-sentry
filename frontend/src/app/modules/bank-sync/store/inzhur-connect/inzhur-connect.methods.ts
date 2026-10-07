import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type InzhurConnectState} from './inzhur-connect.state';

export function inzhurConnectMethods(store: WritableStateSource<InzhurConnectState>) {
  return {
    editCredentials(): void {
      patchState(store, {editingCredentials: true, errorCode: null});
    },
    /** Leaves the code step; a new sign-in (and SMS) only starts when the owner asks again. */
    backToCredentials(): void {
      patchState(store, {step: 'credentials', codeExpiresAt: null, errorCode: null});
    },
  };
}
