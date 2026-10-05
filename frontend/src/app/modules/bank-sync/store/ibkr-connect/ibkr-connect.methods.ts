import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type IbkrConnectPath, type IbkrConnectState} from './ibkr-connect.state';

export function ibkrConnectMethods(store: WritableStateSource<IbkrConnectState>) {
  return {
    setPath(path: IbkrConnectPath): void {
      patchState(store, {path});
    },
    resetValidation(): void {
      patchState(store, {validationStatus: 'idle', preview: null, errorCode: null});
    },
  };
}
