import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {type PaydayTransfer} from '../../models/payday-transfer/payday-transfer.model';

interface StateSignals {
  transfer: Signal<Nullable<PaydayTransfer>>;
}

export function paydayTransferComputed(store: StateSignals) {
  return {
    // No connected Monobank credit card means nothing to cover; a failed load hides the tile too, since it is
    // supplementary and never shouts over the page.
    visible: computed(() => store.transfer()?.hasCard === true),
    hasRate: computed(() => store.transfer()?.rate != null),
  };
}
