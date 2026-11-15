import {type ThesisDto} from '../../../../shared/models/thesis/thesis.model';
import {type ThesisRevisionDto} from '../../../../shared/models/thesis-revision/thesis-revision.model';

export interface ThesisRevisionsState {
  /** Awaiting the owner's verdict, oldest first. */
  pending: ThesisRevisionDto[];
  /** Closed in the last 30 days (confirmed, rejected, expired or superseded). */
  decided: ThesisRevisionDto[];
  /** The owner's theses, which a revision card reads as its "before"; empty when that read failed. */
  theses: ThesisDto[];
}

export const initialThesisRevisionsState: ThesisRevisionsState = {
  pending: [],
  decided: [],
  theses: [],
};
