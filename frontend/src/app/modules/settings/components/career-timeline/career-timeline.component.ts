import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  ViewContainerRef,
} from '@angular/core';
import {
  AlertComponent,
  AsyncStateComponent,
  ButtonComponent,
  CardComponent,
  CmnDialogService,
  ConfirmDialogComponent,
  IconComponent,
  ListItemRowComponent,
  MenuComponent,
  SkeletonComponent,
} from '@lifekit-hq/ui';
import {take} from 'rxjs';

import {MoneyPipe} from '../../../../shared/pipes/money.pipe';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {DialogUtils} from '../../../../shared/utils/dialog.utils';
import {
  CAREER_EVENT_COUNTED_MENU,
  CAREER_EVENT_MENU_ACTION_DELETE,
  CAREER_EVENT_MENU_ACTION_EDIT,
  CAREER_EVENT_MENU_ACTION_RETIRE,
  CAREER_EVENT_SKELETON_ROWS,
  CAREER_EVENT_UNCOUNTED_MENU,
  CAREER_TIMELINE_DISCLOSURE,
} from '../../constants/career-event/career-event.constants';
import {
  type CareerEventDialogData,
  type CareerEventInput,
  type CareerEventRow,
} from '../../models/career-event/career-event.model';
import {CareerEventsStore} from '../../store/career-events/career-events.store';
import {CareerEventDialogComponent} from '../career-event-dialog/career-event-dialog.component';

/** The career timeline in Settings → Money profile: dated events that build a scenario beside the baseline projections. */
@Component({
  selector: 'fns-career-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AlertComponent,
    AsyncStateComponent,
    ButtonComponent,
    CardComponent,
    IconComponent,
    ListItemRowComponent,
    MenuComponent,
    MoneyPipe,
    SkeletonComponent,
  ],
  providers: [CareerEventsStore],
  templateUrl: './career-timeline.component.html',
})
export class CareerTimelineComponent {
  private readonly dialog = inject(CmnDialogService);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly connectivity = inject(ConnectivityService);

  public readonly store = inject(CareerEventsStore);
  // Offline only reaches `resolve` while nothing is on screen: with rows, a failed reload keeps its error banner.
  public readonly view = computed(() => {
    const hasData = this.store.rows().length > 0;
    return AsyncViewUtils.resolve({
      isLoading: this.store.isLoading(),
      hasData,
      errorMessage: this.store.eventsErrorMessage(),
      offline: this.connectivity.offline() && !hasData,
      lastSyncedAt: null,
    });
  });
  public readonly skeletonRowCount = CAREER_EVENT_SKELETON_ROWS;
  public readonly countedMenu = CAREER_EVENT_COUNTED_MENU;
  public readonly uncountedMenu = CAREER_EVENT_UNCOUNTED_MENU;
  public readonly disclosure = CAREER_TIMELINE_DISCLOSURE;

  /** Adds an event, or edits `event`; the reload brings the saved list back. */
  public openEvent(event: CareerEventRow | null = null): void {
    const data: CareerEventDialogData = {event};
    this.dialog
      .open<CareerEventInput>(CareerEventDialogComponent, {
        title: event ? `Edit ${event.label}` : 'Add a career event',
        size: 'sm',
        viewContainerRef: this.viewContainerRef,
        data,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(input => {
        if (input) {
          this.store.save({id: event?.id ?? null, input});
        }
      });
  }

  public onAction(event: CareerEventRow, action: string): void {
    if (action === CAREER_EVENT_MENU_ACTION_EDIT) {
      this.openEvent(event);
    } else if (action === CAREER_EVENT_MENU_ACTION_RETIRE) {
      this.confirm(
        `Retire ${event.label}?`,
        'It stops shaping the scenario and keeps its history. You can still edit or delete it.',
        'Retire',
        () => this.store.retire(event.id)
      );
    } else if (action === CAREER_EVENT_MENU_ACTION_DELETE) {
      this.confirm(
        `Delete ${event.label}?`,
        'It is removed for good, including its history.',
        'Delete',
        () => this.store.remove(event.id)
      );
    }
  }

  private confirm(
    title: string,
    message: string,
    confirmLabel: string,
    onConfirm: () => void
  ): void {
    this.dialog
      .open<boolean>(ConfirmDialogComponent, {
        data: {title, message, confirmLabel, cancelLabel: 'Cancel', confirmVariant: 'destructive'},
        title: DialogUtils.phoneTitle(title),
        size: 'sm',
        phone: 'action-sheet',
        viewContainerRef: this.viewContainerRef,
      })
      .afterClosed()
      .pipe(take(1))
      .subscribe(confirmed => {
        if (confirmed === true) {
          onConfirm();
        }
      });
  }
}
