import {type Nullable} from '@lifekit-hq/ui';

import {type RecommendationDto} from '../../../../shared/models/recommendation/recommendation.model';
import {type ScorecardDto} from '../../../../shared/models/scorecard/scorecard.model';

export interface DecisionsState {
  /** Awaiting the owner's verdict, oldest first. */
  pending: RecommendationDto[];
  /** Closed in the last 30 days (decided, expired or superseded), newest first. */
  decided: RecommendationDto[];
  /** Research scorecards of the selected proposal's ticker, by ticker; a ticker whose read failed is absent. */
  scorecards: Record<string, ScorecardDto>;
  /** The proposal a detail page is open on; null on the queue. */
  selectedId: Nullable<string>;
}

export const initialDecisionsState: DecisionsState = {
  pending: [],
  decided: [],
  scorecards: {},
  selectedId: null,
};
