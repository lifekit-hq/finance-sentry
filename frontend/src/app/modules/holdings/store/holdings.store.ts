import {withAsyncStatus, withUrlSync} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {NULLABLE_STRING_PARAM_CODEC} from '../../../shared/constants/url-params/url-params.constants';
import {holdingsComputed} from './holdings.computed';
import {holdingsEffects, holdingsHooks} from './holdings.effects';
import {holdingsMethods} from './holdings.methods';
import {initialHoldingsState} from './holdings.state';

export const HoldingsStore = signalStore(
  withState(initialHoldingsState),
  withUrlSync({
    provider: {
      param: 'provider',
      default: initialHoldingsState.provider,
      codec: NULLABLE_STRING_PARAM_CODEC,
    },
  }),
  withAsyncStatus({defaultErrorMessage: 'Failed to load positions.'}),
  withMethods(holdingsMethods),
  withComputed(holdingsComputed),
  withMethods(holdingsEffects),
  withHooks(holdingsHooks)
);
