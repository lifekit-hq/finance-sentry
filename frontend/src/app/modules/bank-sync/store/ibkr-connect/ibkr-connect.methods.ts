import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type IbkrConnectPath, type IbkrConnectState} from './ibkr-connect.state';

interface MethodsStore extends WritableStateSource<IbkrConnectState> {
  setIdle: () => void;
}

export function ibkrConnectMethods(store: MethodsStore) {
  return {
    setPath(path: IbkrConnectPath): void {
      patchState(store, {path});
    },
    resetValidation(): void {
      patchState(store, {preview: null});
      store.setIdle();
    },
  };
}
