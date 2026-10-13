import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {DECISIONS_LOAD_FAILED_MESSAGE} from '../../constants/recommendation/recommendation.constants';
import {decisionsComputed} from './decisions.computed';
import {decisionsEffects, decisionsHooks} from './decisions.effects';
import {decisionsMethods} from './decisions.methods';
import {initialDecisionsState} from './decisions.state';

/** The Decisions queue on the Ledger tab; page-scoped, it loads on open. */
export const DecisionsStore = signalStore(
  withState(initialDecisionsState),
  withAsyncStatus({defaultErrorMessage: DECISIONS_LOAD_FAILED_MESSAGE}),
  withMethods(decisionsMethods),
  withComputed(decisionsComputed),
  withMethods(decisionsEffects),
  withHooks({onInit: decisionsHooks})
);
