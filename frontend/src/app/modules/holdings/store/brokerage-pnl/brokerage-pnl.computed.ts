import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {type BrokeragePnl} from '../../models/brokerage-pnl/brokerage-pnl.model';
import {BrokeragePnlUtils} from '../../utils/brokerage-pnl.utils';

interface StateSignals {
  pnl: Signal<Nullable<BrokeragePnl>>;
}

export function brokeragePnlComputed(store: StateSignals) {
  return {
    /** The header rows; empty until the first response. */
    rows: computed(() => {
      const pnl = store.pnl();
      return pnl ? BrokeragePnlUtils.rows(pnl) : [];
    }),
    currency: computed(() => store.pnl()?.currency ?? 'USD'),
  };
}
