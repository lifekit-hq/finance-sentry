import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type FireProjection} from '../../models/fire/fire.model';
import {type FireState} from './fire.state';

export function fireMethods(store: WritableStateSource<FireState>) {
  return {
    setLoading(): void {
      patchState(store, {status: 'loading', errorCode: null});
    },
    setProjection(projection: FireProjection): void {
      patchState(store, {projection, status: 'idle', errorCode: null});
    },
    setError(errorCode: Nullable<string>): void {
      patchState(store, {status: 'error', errorCode});
    },
  };
}
