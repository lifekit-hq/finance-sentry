import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type BrokeragePnl} from '../../models/brokerage-pnl/brokerage-pnl.model';
import {type BrokeragePnlState} from './brokerage-pnl.state';

export function brokeragePnlMethods(store: WritableStateSource<BrokeragePnlState>) {
  return {
    setPnl(pnl: BrokeragePnl): void {
      patchState(store, {pnl});
    },
  };
}
