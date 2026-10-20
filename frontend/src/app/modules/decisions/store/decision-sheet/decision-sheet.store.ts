import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withMethods} from '@ngrx/signals';

import {DECISION_FAILED_MESSAGE} from '../../constants/recommendation/recommendation.constants';
import {decisionSheetEffects} from './decision-sheet.effects';

/** Scoped to one Confirm or Reject sheet; it reads the proposal and verdict from the drawer data. */
export const DecisionSheetStore = signalStore(
  withAsyncStatus({defaultErrorMessage: DECISION_FAILED_MESSAGE}),
  withMethods(decisionSheetEffects)
);
