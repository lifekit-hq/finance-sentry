import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type NetWorthProjection} from '../../models/projection/projection.model';
import {type NetWorthProjectionState} from './net-worth-projection.state';

export function netWorthProjectionMethods(store: WritableStateSource<NetWorthProjectionState>) {
  return {
    setProjection(projection: NetWorthProjection): void {
      patchState(store, {projection});
    },
  };
}
