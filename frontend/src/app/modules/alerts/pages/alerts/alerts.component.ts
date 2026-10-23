import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
  linkedSignal,
} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Router} from '@angular/router';
import {
  AlertItemComponent,
  type AlertItemSeverity,
  ASYNC_STATE_MIN_DISPLAY_MS,
  ASYNC_STATE_SHOW_DELAY_MS,
  AsyncStateComponent,
  CmnPageActionsService,
  EmptyStateComponent,
  type FilterBarChip,
  FilterBarComponent,
  FilterBarSheetDirective,
  ListSectionComponent,
  type LucideIconName,
  PageContainerComponent,
  PageScrollComponent,
  SkeletonComponent,
  ToastService,
} from '@lifekit-hq/ui';

import {ALERTS_MARK_ALL_READ_ACTION} from '../../../../shared/constants/page-actions/page-actions.constants';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {AsyncViewUtils} from '../../../../shared/utils/async-view.utils';
import {AlertFilterSheetComponent} from '../../components/alert-filter-sheet/alert-filter-sheet.component';
import {ALERT_FILTER_LABELS} from '../../constants/alert/alert-filter.constants';
import {type Alert, type AlertSeverity, type AlertType} from '../../models/alert/alert.model';
import {AlertCaptionPipe} from '../../pipes/alert-caption.pipe';
import {AlertMessagePipe} from '../../pipes/alert-message.pipe';
import {AlertsStore} from '../../store/alerts/alerts.store';
import {AlertDayUtils} from '../../utils/alert-day.utils';
import {AlertDestinationUtils} from '../../utils/alert-destination.utils';
import {AlertSeverityUtils} from '../../utils/alert-severity.utils';
import {AlertTypeUtils} from '../../utils/alert-type.utils';
import {RESOLVED_ALERT_ROW_CLASS} from './alerts.constants';

const SKELETON_SECTIONS = 2;
const SKELETON_ROWS_PER_SECTION = 3;
const ALERT_FILTER_CHIP_ID = 'filter';

@Component({
  selector: 'fns-alerts',
  imports: [
    PageContainerComponent,
    PageScrollComponent,
    AlertCaptionPipe,
    AlertItemComponent,
    AlertMessagePipe,
    AlertFilterSheetComponent,
    AsyncStateComponent,
    EmptyStateComponent,
    FilterBarComponent,
    FilterBarSheetDirective,
    ListSectionComponent,
    SkeletonComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  host: {class: 'block md:h-full'},
  templateUrl: './alerts.component.html',
})
export class AlertsComponent {
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly connectivity = inject(ConnectivityService);
  private readonly searching = computed(() => this.store.search().trim() !== '');

  public readonly store = inject(AlertsStore);
  public readonly resolvedRowClass = RESOLVED_ALERT_ROW_CLASS;
  public readonly skeletonSections = Array.from({length: SKELETON_SECTIONS});
  public readonly skeletonRowsPerSection = SKELETON_ROWS_PER_SECTION;
  public readonly showDelay = ASYNC_STATE_SHOW_DELAY_MS;
  public readonly minDisplay = ASYNC_STATE_MIN_DISPLAY_MS;

  // A reload for a new filter or search keeps the previous rows on screen, dimmed, until the new
  // first page lands (`keepContent`); a failed reload drops them and shows the error in place.
  public readonly view = computed(() =>
    AsyncViewUtils.resolve({
      isLoading: this.store.isLoading(),
      hasData: this.store.alerts().length > 0,
      keepContent: true,
      errorMessage: this.store.errorMessage() ?? '',
      offline: this.connectivity.offline(),
      lastSyncedAt: null,
    })
  );

  public readonly alertsSubtitle = computed(() => {
    const count = this.store.unreadCount();
    if (count > 0) {
      return `${count} unread ${count === 1 ? 'alert' : 'alerts'}`;
    }
    return 'All caught up';
  });

  /** The search box's text; the store debounces it into the query. */
  public readonly query = linkedSignal(() => this.store.search());

  public readonly dayGroups = computed(() => AlertDayUtils.groupByDay(this.store.alerts()));

  public readonly emptyTitle = computed(() => {
    if (this.searching()) {
      return 'No matching alerts';
    }
    return this.store.filter() === 'all' ? 'No alerts' : `No ${this.store.filter()} alerts`;
  });

  public readonly emptySubtext = computed(() => {
    if (this.searching()) {
      return 'Try a different search or filter.';
    }
    return this.store.filter() === 'all'
      ? 'All your accounts are healthy.'
      : 'Try a different filter.';
  });

  /** Only a narrowed list has a way out; an empty inbox has nothing to widen. */
  public readonly canShowAll = computed(() => this.searching() || this.store.filter() !== 'all');

  public readonly activeFilterCount = computed(() => (this.store.filter() === 'all' ? 0 : 1));
  public readonly appliedFilters = computed<FilterBarChip[]>(() =>
    this.store.filter() === 'all'
      ? []
      : [{id: ALERT_FILTER_CHIP_ID, label: ALERT_FILTER_LABELS[this.store.filter()]}]
  );

  constructor() {
    inject(CmnPageActionsService)
      .on(ALERTS_MARK_ALL_READ_ACTION)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.markAllRead());
    this.store.applySearch(this.query);
  }

  public clearFilter(): void {
    this.store.setFilter('all');
  }

  /** The empty state's way out: back to the unfiltered, unsearched list. */
  public showAll(): void {
    this.clearFilter();
    this.query.set('');
  }

  public iconFor(type: AlertType): LucideIconName {
    return AlertTypeUtils.meta(type).icon;
  }

  public typeLabel(type: AlertType): string {
    return AlertTypeUtils.meta(type).label;
  }

  public severityFor(severity: AlertSeverity): AlertItemSeverity {
    return AlertSeverityUtils.toItemSeverity(severity);
  }

  public markAllRead(): void {
    if (this.store.unreadCount() === 0) {
      return;
    }
    this.store.markAllRead();
    this.toast.show('All alerts marked as read', 'success');
  }

  public dismiss(id: string): void {
    this.store.dismiss(id);
    this.toast.show('Alert dismissed', 'info');
  }

  public openAlert(item: Alert): void {
    if (!item.isRead) {
      this.store.markRead(item.id);
    }
    const navigation = AlertDestinationUtils.resolve(item);
    if (navigation?.kind === 'external') {
      window.open(navigation.url, '_blank', 'noopener,noreferrer');
    } else if (navigation?.kind === 'url') {
      void this.router.navigateByUrl(navigation.url);
    } else if (navigation) {
      void this.router.navigate(navigation.commands);
    }
  }
}
