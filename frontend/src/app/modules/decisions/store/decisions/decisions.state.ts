import {type RecommendationDto} from '../../../../shared/models/recommendation/recommendation.model';

export interface DecisionsState {
  /** Awaiting the owner's verdict, oldest first. */
  pending: RecommendationDto[];
  /** Closed in the last 30 days (decided, expired or superseded), newest first. */
  decided: RecommendationDto[];
}

export const initialDecisionsState: DecisionsState = {
  pending: [],
  decided: [],
};
