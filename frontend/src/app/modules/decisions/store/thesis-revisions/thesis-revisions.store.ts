import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {THESIS_REVISIONS_LOAD_FAILED_MESSAGE} from '../../constants/thesis-revision/thesis-revision.constants';
import {thesisRevisionsComputed} from './thesis-revisions.computed';
import {thesisRevisionsEffects, thesisRevisionsHooks} from './thesis-revisions.effects';
import {thesisRevisionsMethods} from './thesis-revisions.methods';
import {initialThesisRevisionsState} from './thesis-revisions.state';

/** The thesis changes Ledger proposes, on the Decisions queue; page-scoped, it loads on open. */
export const ThesisRevisionsStore = signalStore(
  withState(initialThesisRevisionsState),
  withAsyncStatus({defaultErrorMessage: THESIS_REVISIONS_LOAD_FAILED_MESSAGE}),
  withMethods(thesisRevisionsMethods),
  withComputed(thesisRevisionsComputed),
  withMethods(thesisRevisionsEffects),
  withHooks({onInit: thesisRevisionsHooks})
);
