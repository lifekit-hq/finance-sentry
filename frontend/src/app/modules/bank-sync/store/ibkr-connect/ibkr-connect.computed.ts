import {computed, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';

import {type IbkrConnectState} from './ibkr-connect.state';

interface StateSignals {
  status: Signal<AsyncStatus>;
  preview: Signal<IbkrConnectState['preview']>;
}

export function ibkrConnectComputed(store: StateSignals) {
  return {
    hasPreview: computed(() => store.status() === 'success' && !!store.preview()),
    /** A report with no positions or no cash rows usually means a section is unticked in the query. */
    missingSections: computed(() => {
      const preview = store.preview();
      if (!preview) {
        return [];
      }
      const missing: string[] = [];
      if (preview.openPositionsCount === 0) {
        missing.push('Open Positions');
      }
      if (preview.cashCurrencies.length === 0) {
        missing.push('Cash Report');
      }
      return missing;
    }),
  };
}
