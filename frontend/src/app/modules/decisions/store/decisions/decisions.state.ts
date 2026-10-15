import {type RecommendationDto} from '../../../../shared/models/recommendation/recommendation.model';
import {type ScorecardDto} from '../../../../shared/models/scorecard/scorecard.model';

export interface DecisionsState {
  /** Awaiting the owner's verdict, oldest first. */
  pending: RecommendationDto[];
  /** Closed in the last 30 days (decided, expired or superseded), newest first. */
  decided: RecommendationDto[];
  /** Research scorecards of the pending tickers, by ticker; a ticker whose read failed is absent. */
  scorecards: Record<string, ScorecardDto>;
}

export const initialDecisionsState: DecisionsState = {
  pending: [],
  decided: [],
  scorecards: {},
};
