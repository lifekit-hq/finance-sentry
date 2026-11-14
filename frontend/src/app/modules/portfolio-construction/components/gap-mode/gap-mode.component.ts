import {DecimalPipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject, input, output} from '@angular/core';
import {RouterLink} from '@angular/router';
import {
  AlertComponent,
  AsyncStateComponent,
  CardComponent,
  type Nullable,
  SkeletonComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {
  VERDICT_WORD_LABEL,
  VERDICT_WORD_VARIANT,
} from '../../../../shared/constants/scorecard/scorecard.constants';
import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {
  GAP_HELD_OUTSIDE_LABEL,
  GAP_NAMES_HEADING,
  GAP_NO_BOOK_COPY,
  GAP_SECTORS_HEADING,
  GAP_SKELETON_ROWS,
  GAP_SLEEVES_HEADING,
  GAP_STATUS_LABEL,
  GAP_STATUS_VARIANT,
  GAP_TITLE,
} from '../../constants/gap-mode/gap-mode.constants';
import {type GapMode} from '../../models/gap-mode/gap-mode.model';
import {GapModeUtils} from '../../utils/gap-mode.utils';

/**
 * Gap mode (spec 726 US4): the book held against the constructed target, as missing / underweight / overweight
 * names with amounts and sectors. Presentational: the page's store loads it. Informational; no line is an order.
 */
@Component({
  selector: 'fns-gap-mode',
  imports: [
    AlertComponent,
    AsyncStateComponent,
    CardComponent,
    DecimalPipe,
    MoneyPipe,
    RouterLink,
    SkeletonComponent,
    TagComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './gap-mode.component.html',
})
export class GapModeComponent {
  private readonly connectivity = inject(ConnectivityService);

  public readonly gap = input<Nullable<GapMode>>(null);
  public readonly loading = input(false);
  public readonly errorMessage = input<string>('');
  public readonly retry = output();

  public readonly title = GAP_TITLE;
  public readonly noBookCopy = GAP_NO_BOOK_COPY;
  public readonly heldOutsideLabel = GAP_HELD_OUTSIDE_LABEL;
  public readonly namesHeading = GAP_NAMES_HEADING;
  public readonly sleevesHeading = GAP_SLEEVES_HEADING;
  public readonly sectorsHeading = GAP_SECTORS_HEADING;
  public readonly skeletonRowCount = GAP_SKELETON_ROWS;
  public readonly statusLabel = GAP_STATUS_LABEL;
  public readonly statusVariant = GAP_STATUS_VARIANT;
  public readonly wordLabel = VERDICT_WORD_LABEL;
  public readonly wordVariant = VERDICT_WORD_VARIANT;
  public readonly gapText = GapModeUtils.gapText;

  public readonly view = computed(() => {
    const hasData = this.gap() !== null;
    return AsyncViewUtils.resolve({
      isLoading: this.loading(),
      hasData,
      errorMessage: this.errorMessage(),
      offline: this.connectivity.offline() && !hasData,
      lastSyncedAt: null,
    });
  });
}
