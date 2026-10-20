import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, input, output} from '@angular/core';
import {RouterLink} from '@angular/router';
import {ButtonComponent, CardComponent, type Nullable, TagComponent} from '@lifekit-hq/ui';

import {ScorecardVerdictComponent} from '../../../../shared/components/scorecard-verdict/scorecard-verdict.component';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {
  type RecommendationDto,
  type RecommendationVerdict,
} from '../../../../shared/models/recommendation/recommendation.model';
import {type ScorecardDto} from '../../../../shared/models/scorecard/scorecard.model';
import {
  RECOMMENDATION_ACTION_LABEL,
  RECOMMENDATION_ACTION_VARIANT,
  RECOMMENDATION_CONFIDENCE_LABEL,
  RECOMMENDATION_EVIDENCE_LABEL,
  RECOMMENDATION_TRIGGER_LABEL,
} from '../../constants/recommendation/recommendation.constants';
import {RecommendationSizePipe} from '../../pipes/recommendation.pipe';

/**
 * One pending proposal: the move and its size, why (with the numbers), confidence, what would invalidate it,
 * the evidence it cites and when it expires. Reject and Confirm open the decision sheet through the host's store.
 * With a scorecard, the ticker's research verdict and its drill-down sit under the Why (spec 726 US2).
 */
@Component({
  selector: 'fns-recommendation-card',
  imports: [
    ButtonComponent,
    CardComponent,
    DatePipe,
    RecommendationSizePipe,
    RouterLink,
    ScorecardVerdictComponent,
    TagComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  templateUrl: './recommendation-card.component.html',
})
export class RecommendationCardComponent {
  public readonly recommendation = input.required<RecommendationDto>();
  /** Off on the asset dossier, which is already the ticker's page. */
  public readonly linkTicker = input(false);
  /** The ticker's scorecard and verdict word; absent while it loads or when it could not be read. */
  public readonly scorecard = input<Nullable<ScorecardDto>>(null);
  public readonly decide = output<RecommendationVerdict>();

  public readonly dossierRoute = AppRoute.AssetDossier;
  public readonly actionLabel = RECOMMENDATION_ACTION_LABEL;
  public readonly actionVariant = RECOMMENDATION_ACTION_VARIANT;
  public readonly confidenceLabel = RECOMMENDATION_CONFIDENCE_LABEL;
  public readonly triggerLabel = RECOMMENDATION_TRIGGER_LABEL;
  public readonly evidenceLabel = RECOMMENDATION_EVIDENCE_LABEL;
}
