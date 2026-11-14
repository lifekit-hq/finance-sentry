import {DatePipe, DecimalPipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {FormsModule} from '@angular/forms';
import {RouterLink} from '@angular/router';
import {
  AlertComponent,
  AsyncStateComponent,
  CardComponent,
  ChipComponent,
  EmptyStateComponent,
  PageComponent,
  PageScrollComponent,
  SegmentedComponent,
  SelectComponent,
  SkeletonComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {NOT_ADVICE_NOTICE} from '../../../../shared/constants/legal/legal.constants';
import {
  VERDICT_WORD_LABEL,
  VERDICT_WORD_VARIANT,
} from '../../../../shared/constants/scorecard/scorecard.constants';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {GapModeComponent} from '../../components/gap-mode/gap-mode.component';
import {
  UNIVERSE_ARCHETYPE_LABEL,
  UNIVERSE_EMPTY_COPY,
  UNIVERSE_FILTER_LABEL,
  UNIVERSE_NO_MATCH_COPY,
  UNIVERSE_SKELETON_ROWS,
  UNIVERSE_SORT_LABEL,
  UNIVERSE_SORT_OPTIONS,
  UNIVERSE_SOURCE_LABEL,
  UNIVERSE_TITLE_NOTE,
  UNIVERSE_TRUNCATED_NOTE,
  UNIVERSE_VERDICT_FILTERS,
  UNIVERSE_WEIGHT_NOTE,
} from '../../constants/universe-screen/universe-screen.constants';
import {
  type UniverseSortKey,
  type UniverseVerdictFilter,
} from '../../models/universe-screen/universe-screen.model';
import {UniverseScreenStore} from '../../store/universe-screen/universe-screen.store';

@Component({
  selector: 'fns-universe-screen',
  imports: [
    AlertComponent,
    AsyncStateComponent,
    CardComponent,
    ChipComponent,
    DatePipe,
    DecimalPipe,
    EmptyStateComponent,
    FormsModule,
    GapModeComponent,
    PageComponent,
    PageScrollComponent,
    RouterLink,
    SegmentedComponent,
    SelectComponent,
    SkeletonComponent,
    TagComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UniverseScreenStore],
  templateUrl: './universe-screen.component.html',
})
export class UniverseScreenComponent {
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(UniverseScreenStore);

  public readonly titleNote = UNIVERSE_TITLE_NOTE;
  public readonly notAdvice = NOT_ADVICE_NOTICE;
  public readonly weightNote = UNIVERSE_WEIGHT_NOTE;
  public readonly truncatedNote = UNIVERSE_TRUNCATED_NOTE;
  public readonly emptyCopy = UNIVERSE_EMPTY_COPY;
  public readonly noMatchCopy = UNIVERSE_NO_MATCH_COPY;
  public readonly archetypeLabel = UNIVERSE_ARCHETYPE_LABEL;
  public readonly sortLabel = UNIVERSE_SORT_LABEL;
  public readonly filterLabel = UNIVERSE_FILTER_LABEL;
  public readonly sortOptions = UNIVERSE_SORT_OPTIONS;
  public readonly verdictFilters = UNIVERSE_VERDICT_FILTERS;
  public readonly sourceLabel = UNIVERSE_SOURCE_LABEL;
  public readonly skeletonRowCount = UNIVERSE_SKELETON_ROWS;
  public readonly wordLabel = VERDICT_WORD_LABEL;
  public readonly wordVariant = VERDICT_WORD_VARIANT;

  public readonly view = computed(() => {
    const hasData = this.store.screen() !== null;
    return AsyncViewUtils.resolve({
      isLoading: this.store.isLoadLoading(),
      hasData,
      errorMessage: this.store.loadErrorMessage(),
      offline: this.connectivity.offline() && !hasData,
      lastSyncedAt: null,
    });
  });
  public readonly isEmpty = computed(() => !this.store.hasRows());
  public readonly noMatch = computed(() => this.store.hasRows() && this.store.rows().length === 0);

  public chooseArchetype(key: string): void {
    if (key === this.store.screen()?.archetype) {
      return;
    }
    this.store.setArchetype(key);
    this.store.load();
  }

  public chooseSort(key: unknown): void {
    this.store.setSortKey(key as UniverseSortKey);
  }

  public chooseFilter(filter: UniverseVerdictFilter): void {
    this.store.setVerdictFilter(filter);
  }
}
