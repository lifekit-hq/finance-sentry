import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {
  AsyncStateComponent,
  CardComponent,
  ListItemRowComponent,
  PageComponent,
  PageScrollComponent,
  ProseComponent,
  SkeletonComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {type RecommendationDto} from '../../../../shared/models/recommendation/recommendation.model';
import {
  DECISION_SKELETON_DECIDED_ROWS,
  DECISION_SKELETON_OUTCOME_LINES,
  DECISION_SKELETON_PENDING_ROWS,
  DECISIONS_EMPTY_COPY,
  RECOMMENDATION_ACTION_LABEL,
  RECOMMENDATION_ACTION_VARIANT,
  RECOMMENDATION_STATUS_LABEL,
  RECOMMENDATION_STATUS_VARIANT,
  RECOMMENDATION_TRIGGER_LABEL,
} from '../../constants/recommendation/recommendation.constants';
import {
  RecommendationExcessPipe,
  RecommendationExcessTonePipe,
  RecommendationOutcomePipe,
  RecommendationRowValuePipe,
} from '../../pipes/recommendation.pipe';
import {DecisionsStore} from '../../store/decisions/decisions.store';
import {type OutcomeTone} from '../../utils/recommendation.utils';

/** The Decisions queue on the Ledger tab: pending proposals oldest first, one row each that pushes its detail, then the last 30 days' outcomes. */
@Component({
  selector: 'fns-decisions',
  imports: [
    AsyncStateComponent,
    CardComponent,
    DatePipe,
    ListItemRowComponent,
    PageComponent,
    PageScrollComponent,
    ProseComponent,
    RecommendationExcessPipe,
    RecommendationExcessTonePipe,
    RecommendationOutcomePipe,
    RecommendationRowValuePipe,
    RouterLink,
    SkeletonComponent,
    TagComponent,
  ],
  providers: [DecisionsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'flex min-h-0 flex-1 flex-col'},
  templateUrl: './decisions.component.html',
})
export class DecisionsComponent {
  private readonly router = inject(Router);

  public readonly store = inject(DecisionsStore);

  public readonly dossierRoute = AppRoute.AssetDossier;
  public readonly emptyCopy = DECISIONS_EMPTY_COPY;
  public readonly skeletonPendingRows = DECISION_SKELETON_PENDING_ROWS;
  public readonly skeletonDecidedRows = Array.from({length: DECISION_SKELETON_DECIDED_ROWS});
  public readonly skeletonOutcomeLines = DECISION_SKELETON_OUTCOME_LINES;
  public readonly actionLabel = RECOMMENDATION_ACTION_LABEL;
  public readonly actionVariant = RECOMMENDATION_ACTION_VARIANT;
  public readonly triggerLabel = RECOMMENDATION_TRIGGER_LABEL;
  public readonly statusLabel = RECOMMENDATION_STATUS_LABEL;
  public readonly statusVariant = RECOMMENDATION_STATUS_VARIANT;
  public readonly toneClass: Readonly<Record<OutcomeTone, string>> = {
    gain: GAIN_TEXT_CLASS,
    loss: LOSS_TEXT_CLASS,
  };

  /** Pushes the proposal's detail, where Reject and Confirm are pinned. */
  public open(rec: RecommendationDto): void {
    void this.router.navigate([AppRoute.LedgerDecision, rec.id]);
  }
}
