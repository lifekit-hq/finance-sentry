import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type FireProjection} from '../../models/fire/fire.model';
import {type FireState} from './fire.state';

export function fireMethods(store: WritableStateSource<FireState>) {
  return {
    setProjection(projection: FireProjection): void {
      patchState(store, {projection});
    },
  };
}
