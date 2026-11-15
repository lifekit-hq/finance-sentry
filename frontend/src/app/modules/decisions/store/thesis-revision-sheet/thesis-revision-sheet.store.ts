import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withMethods, withState} from '@ngrx/signals';

import {THESIS_REVISION_FAILED_MESSAGE} from '../../constants/thesis-revision/thesis-revision.constants';
import {thesisRevisionSheetEffects} from './thesis-revision-sheet.effects';
import {thesisRevisionSheetMethods} from './thesis-revision-sheet.methods';
import {initialThesisRevisionSheetState} from './thesis-revision-sheet.state';

/** Scoped to one Confirm or Reject sheet; it reads the revision and verdict from the drawer data. */
export const ThesisRevisionSheetStore = signalStore(
  withState(initialThesisRevisionSheetState),
  withAsyncStatus({defaultErrorMessage: THESIS_REVISION_FAILED_MESSAGE}),
  withMethods(thesisRevisionSheetMethods),
  withMethods(thesisRevisionSheetEffects)
);
