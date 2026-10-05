import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {type IbkrConnectState, type IbkrValidationStatus} from './ibkr-connect.state';

interface StateSignals {
  validationStatus: Signal<IbkrValidationStatus>;
  errorCode: Signal<IbkrConnectState['errorCode']>;
  preview: Signal<IbkrConnectState['preview']>;
}

const DEFAULT_VALIDATION_ERROR =
  "Couldn't check your Flex query. Check the token and query ID and try again.";

export function ibkrConnectComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    isValidating: computed(() => store.validationStatus() === 'validating'),
    hasPreview: computed(() => store.validationStatus() === 'validated' && !!store.preview()),
    validationErrorMessage: computed(() => {
      if (store.validationStatus() !== 'error') {
        return '';
      }
      const code = store.errorCode();
      return (code ? errorMessages.resolve(code) : null) ?? DEFAULT_VALIDATION_ERROR;
    }),
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
