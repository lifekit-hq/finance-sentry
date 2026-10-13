import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, input, output} from '@angular/core';
import {RouterLink} from '@angular/router';
import {ButtonComponent, CardComponent, TagComponent} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {
  type RecommendationDto,
  type RecommendationVerdict,
} from '../../../../shared/models/recommendation/recommendation.model';
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
 */
@Component({
  selector: 'fns-recommendation-card',
  imports: [
    ButtonComponent,
    CardComponent,
    DatePipe,
    RecommendationSizePipe,
    RouterLink,
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
  public readonly decide = output<RecommendationVerdict>();

  public readonly dossierRoute = AppRoute.AssetDossier;
  public readonly actionLabel = RECOMMENDATION_ACTION_LABEL;
  public readonly actionVariant = RECOMMENDATION_ACTION_VARIANT;
  public readonly confidenceLabel = RECOMMENDATION_CONFIDENCE_LABEL;
  public readonly triggerLabel = RECOMMENDATION_TRIGGER_LABEL;
  public readonly evidenceLabel = RECOMMENDATION_EVIDENCE_LABEL;
}
