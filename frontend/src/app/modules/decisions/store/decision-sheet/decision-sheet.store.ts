import {signalStore, withComputed, withMethods, withState} from '@ngrx/signals';

import {decisionSheetComputed} from './decision-sheet.computed';
import {decisionSheetEffects} from './decision-sheet.effects';
import {decisionSheetMethods} from './decision-sheet.methods';
import {initialDecisionSheetState} from './decision-sheet.state';

/** Scoped to one Confirm or Reject sheet; it reads the proposal and verdict from the drawer data. */
export const DecisionSheetStore = signalStore(
  withState(initialDecisionSheetState),
  withMethods(decisionSheetMethods),
  withComputed(decisionSheetComputed),
  withMethods(decisionSheetEffects)
);
