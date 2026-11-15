import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {RouterLink} from '@angular/router';
import {AsyncStateComponent, CardComponent, SkeletonComponent, TagComponent} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {
  THESIS_REVISION_KIND_LABEL,
  THESIS_REVISION_SKELETON_ROWS,
  THESIS_REVISION_STATUS_LABEL,
  THESIS_REVISION_STATUS_VARIANT,
  THESIS_REVISIONS_EMPTY_COPY,
} from '../../constants/thesis-revision/thesis-revision.constants';
import {ThesisRevisionsStore} from '../../store/thesis-revisions/thesis-revisions.store';
import {ThesisRevisionCardComponent} from '../thesis-revision-card/thesis-revision-card.component';

/**
 * The thesis changes Ledger proposes, on the Decisions queue: one card per pending change with Confirm and Reject,
 * then the last 30 days' outcomes. Only the owner's confirm writes a thesis.
 */
@Component({
  selector: 'fns-thesis-revisions-section',
  imports: [
    AsyncStateComponent,
    CardComponent,
    DatePipe,
    RouterLink,
    SkeletonComponent,
    TagComponent,
    ThesisRevisionCardComponent,
  ],
  providers: [ThesisRevisionsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  templateUrl: './thesis-revisions-section.component.html',
})
export class ThesisRevisionsSectionComponent {
  public readonly store = inject(ThesisRevisionsStore);

  public readonly dossierRoute = AppRoute.AssetDossier;
  public readonly emptyCopy = THESIS_REVISIONS_EMPTY_COPY;
  public readonly skeletonCards = Array.from({length: THESIS_REVISION_SKELETON_ROWS});
  public readonly kindLabel = THESIS_REVISION_KIND_LABEL;
  public readonly statusLabel = THESIS_REVISION_STATUS_LABEL;
  public readonly statusVariant = THESIS_REVISION_STATUS_VARIANT;
}
