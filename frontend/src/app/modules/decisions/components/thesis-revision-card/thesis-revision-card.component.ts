import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, input, output} from '@angular/core';
import {RouterLink} from '@angular/router';
import {ButtonComponent, CardComponent, type Nullable, TagComponent} from '@lifekit-hq/ui';

import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {type ThesisDto} from '../../../../shared/models/thesis/thesis.model';
import {
  type ThesisRevisionDto,
  type ThesisRevisionVerdict,
} from '../../../../shared/models/thesis-revision/thesis-revision.model';
import {
  THESIS_REVISION_CLOSE_TEXT,
  THESIS_REVISION_KIND_LABEL,
  THESIS_REVISION_KIND_VARIANT,
  THESIS_REVISION_NO_BEFORE_TEXT,
  THESIS_REVISION_PROMOTE_DEFAULTS_TEXT,
  THESIS_REVISION_UNCHANGED_TEXT,
  THESIS_REVISION_WALL_TEXT,
  THESIS_REVISION_WALL_TITLE,
} from '../../constants/thesis-revision/thesis-revision.constants';
import {ThesisRevisionUtils} from '../../utils/thesis-revision.utils';

/**
 * One pending thesis change Ledger proposes: what it would do, why, the thesis before and after (text and triggers),
 * the evidence it rests on and when it expires. Reject and Confirm open the revision sheet through the host's store.
 * Confirming writes the thesis only; it never places or changes a position.
 */
@Component({
  selector: 'fns-thesis-revision-card',
  imports: [ButtonComponent, CardComponent, DatePipe, RouterLink, TagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block'},
  templateUrl: './thesis-revision-card.component.html',
})
export class ThesisRevisionCardComponent {
  public readonly revision = input.required<ThesisRevisionDto>();
  /** The thesis as it stands, which a revision changes; absent on a promotion and while it loads. */
  public readonly thesis = input<Nullable<ThesisDto>>(null);
  /** Off on the asset dossier, which is already the ticker's page. */
  public readonly linkTicker = input(false);
  public readonly decide = output<ThesisRevisionVerdict>();

  public readonly changes = computed(() =>
    ThesisRevisionUtils.changes(this.revision(), this.thesis())
  );
  public readonly usesDefaults = computed(() => ThesisRevisionUtils.usesDefaults(this.revision()));

  public readonly dossierRoute = AppRoute.AssetDossier;
  public readonly kindLabel = THESIS_REVISION_KIND_LABEL;
  public readonly kindVariant = THESIS_REVISION_KIND_VARIANT;
  public readonly wallTitle = THESIS_REVISION_WALL_TITLE;
  public readonly wallText = THESIS_REVISION_WALL_TEXT;
  public readonly closeText = THESIS_REVISION_CLOSE_TEXT;
  public readonly defaultsText = THESIS_REVISION_PROMOTE_DEFAULTS_TEXT;
  public readonly noBeforeText = THESIS_REVISION_NO_BEFORE_TEXT;
  public readonly unchangedText = THESIS_REVISION_UNCHANGED_TEXT;
  public readonly addedClass = GAIN_TEXT_CLASS;
  public readonly removedClass = LOSS_TEXT_CLASS;
}
