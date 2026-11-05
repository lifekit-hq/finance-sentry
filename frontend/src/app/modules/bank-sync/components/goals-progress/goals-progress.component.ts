import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  AlertComponent,
  AsyncStateComponent,
  CardComponent,
  SkeletonComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {
  GOALS_PROGRESS_EMPTY_MESSAGE,
  GOALS_PROGRESS_EMPTY_SUB_MESSAGE,
} from '../../constants/goals-progress/goals-progress.constants';
import {GoalsProgressStore} from '../../store/goals-progress/goals-progress.store';

const SKELETON_ROWS = 2;

/**
 * The IPS goals tracked against net worth plus savings (#454 S2): months left, the saving each needs at
 * the shared return range, and whether the current savings path gets there. Context for the person's own
 * planning, never a forecast or advice.
 */
@Component({
  selector: 'fns-goals-progress',
  imports: [AlertComponent, AsyncStateComponent, CardComponent, SkeletonComponent, TagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [GoalsProgressStore],
  templateUrl: './goals-progress.component.html',
})
export class GoalsProgressComponent {
  public readonly store = inject(GoalsProgressStore);
  public readonly emptyMessage = GOALS_PROGRESS_EMPTY_MESSAGE;
  public readonly emptySubMessage = GOALS_PROGRESS_EMPTY_SUB_MESSAGE;
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS});
}
