import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  AsyncStateComponent,
  ButtonComponent,
  CardComponent,
  PageContainerComponent,
  PageScrollComponent,
  SkeletonComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {RecommendationCardComponent} from '../../components/recommendation-card/recommendation-card.component';
import {
  DECISION_DETAIL_MISSING_COPY,
  RECOMMENDATION_STATUS_LABEL,
  RECOMMENDATION_STATUS_VARIANT,
} from '../../constants/recommendation/recommendation.constants';
import {RecommendationOutcomePipe} from '../../pipes/recommendation.pipe';
import {DecisionsStore} from '../../store/decisions/decisions.store';

/**
 * One proposal pushed from the Decisions queue: the whole record (why, invalidation, evidence, verdict) with
 * Reject and Confirm pinned under it, so the decision is never below the fold.
 */
@Component({
  selector: 'fns-decision-detail',
  imports: [
    AsyncStateComponent,
    ButtonComponent,
    CardComponent,
    DatePipe,
    PageContainerComponent,
    PageScrollComponent,
    RecommendationCardComponent,
    RecommendationOutcomePipe,
    SkeletonComponent,
    TagComponent,
  ],
  providers: [DecisionsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'flex min-h-0 flex-1 flex-col'},
  templateUrl: './decision-detail.component.html',
})
export class DecisionDetailComponent {
  public readonly store = inject(DecisionsStore);

  public readonly missingCopy = DECISION_DETAIL_MISSING_COPY;
  public readonly statusLabel = RECOMMENDATION_STATUS_LABEL;
  public readonly statusVariant = RECOMMENDATION_STATUS_VARIANT;
}
