import {type SubscriptionKind} from '../subscription/subscription.model';

export interface CommitmentDialogData {
  /** Name of the legacy row being linked; absent when the dialog adds a new commitment. */
  linkTo: Nullable<string>;
  /** Kind of the legacy row being linked, so the charge it starts from is read the way that kind is tracked. */
  kind?: SubscriptionKind;
}
