import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type ThesisRiskGateVerdict} from '../../../../shared/models/thesis-revision/thesis-revision.model';
import {type ThesisRevisionSheetState} from './thesis-revision-sheet.state';

export function thesisRevisionSheetMethods(store: WritableStateSource<ThesisRevisionSheetState>) {
  return {
    setRefusal(refusal: Nullable<ThesisRiskGateVerdict>): void {
      patchState(store, {refusal});
    },
  };
}
