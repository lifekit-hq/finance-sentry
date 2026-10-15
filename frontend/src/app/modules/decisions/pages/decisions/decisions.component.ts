import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {RouterLink} from '@angular/router';
import {
  AsyncStateComponent,
  CardComponent,
  PageContainerComponent,
  SkeletonComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {RecommendationCardComponent} from '../../components/recommendation-card/recommendation-card.component';
import {
  DECISION_SKELETON_CARDS,
  DECISION_SKELETON_DECIDED_ROWS,
  DECISION_SKELETON_EVIDENCE_LINES,
  DECISION_SKELETON_FACTS,
  DECISION_SKELETON_OUTCOME_LINES,
  DECISION_SKELETON_TEXT_LINES,
  DECISIONS_EMPTY_COPY,
  RECOMMENDATION_ACTION_LABEL,
  RECOMMENDATION_ACTION_VARIANT,
  RECOMMENDATION_STATUS_LABEL,
  RECOMMENDATION_STATUS_VARIANT,
} from '../../constants/recommendation/recommendation.constants';
import {
  RecommendationExcessPipe,
  RecommendationExcessTonePipe,
  RecommendationOutcomePipe,
} from '../../pipes/recommendation.pipe';
import {DecisionsStore} from '../../store/decisions/decisions.store';
import {type OutcomeTone} from '../../utils/recommendation.utils';

/** The Decisions queue on the Ledger tab: pending proposals oldest first, then the last 30 days' outcomes. */
@Component({
  selector: 'fns-decisions',
  imports: [
    AsyncStateComponent,
    CardComponent,
    DatePipe,
    PageContainerComponent,
    RecommendationCardComponent,
    RecommendationExcessPipe,
    RecommendationExcessTonePipe,
    RecommendationOutcomePipe,
    RouterLink,
    SkeletonComponent,
    TagComponent,
  ],
  providers: [DecisionsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  templateUrl: './decisions.component.html',
})
export class DecisionsComponent {
  public readonly store = inject(DecisionsStore);

  public readonly dossierRoute = AppRoute.AssetDossier;
  public readonly emptyCopy = DECISIONS_EMPTY_COPY;
  public readonly skeletonCards = Array.from({length: DECISION_SKELETON_CARDS});
  public readonly skeletonDecidedRows = Array.from({length: DECISION_SKELETON_DECIDED_ROWS});
  public readonly skeletonFacts = Array.from({length: DECISION_SKELETON_FACTS});
  public readonly skeletonTextLines = DECISION_SKELETON_TEXT_LINES;
  public readonly skeletonEvidenceLines = DECISION_SKELETON_EVIDENCE_LINES;
  public readonly skeletonOutcomeLines = DECISION_SKELETON_OUTCOME_LINES;
  public readonly actionLabel = RECOMMENDATION_ACTION_LABEL;
  public readonly actionVariant = RECOMMENDATION_ACTION_VARIANT;
  public readonly statusLabel = RECOMMENDATION_STATUS_LABEL;
  public readonly statusVariant = RECOMMENDATION_STATUS_VARIANT;
  public readonly toneClass: Readonly<Record<OutcomeTone, string>> = {
    gain: GAIN_TEXT_CLASS,
    loss: LOSS_TEXT_CLASS,
  };
}
