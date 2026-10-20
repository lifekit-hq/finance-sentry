import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {connectComputed} from './connect.computed';
import {connectEffects, connectSuccessRouter} from './connect.effects';
import {connectMethods} from './connect.methods';
import {initialConnectState} from './connect.state';

export const ConnectStore = signalStore(
  withState(initialConnectState),
  withNamedAsyncStatus('connect'),
  withMethods(connectMethods),
  withComputed(connectComputed),
  withMethods(connectEffects),
  withHooks({
    onInit(store): void {
      connectSuccessRouter({
        connectStatus: store.connectStatus,
        institutionType: store.institutionType,
      });
    },
  })
);
