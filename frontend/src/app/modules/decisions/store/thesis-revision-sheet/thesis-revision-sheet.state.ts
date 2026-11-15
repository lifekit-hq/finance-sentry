import {type Nullable} from '@lifekit-hq/ui';

import {type ThesisRiskGateVerdict} from '../../../../shared/models/thesis-revision/thesis-revision.model';

export interface ThesisRevisionSheetState {
  /** The risk gate's reading when it refused a promotion: the proposal stays pending and an override is offered. */
  refusal: Nullable<ThesisRiskGateVerdict>;
}

export const initialThesisRevisionSheetState: ThesisRevisionSheetState = {
  refusal: null,
};
