export type DecisionSheetStatus = 'idle' | 'submitting' | 'error';

export interface DecisionSheetState {
  status: DecisionSheetStatus;
  errorCode: Nullable<string>;
}

export const initialDecisionSheetState: DecisionSheetState = {
  status: 'idle',
  errorCode: null,
};
