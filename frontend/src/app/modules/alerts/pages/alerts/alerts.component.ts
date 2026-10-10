import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {takeUntilDestroyed} from '@angular/core/rxjs-interop';
import {Router} from '@angular/router';
import {
  AlertItemComponent,
  type AlertItemSeverity,
  ButtonComponent,
  CmnPageActionsService,
  EmptyStateComponent,
  type LucideIconName,
  PageContainerComponent,
  ToastService,
} from '@lifekit-hq/ui';

import {ALERTS_MARK_ALL_READ_ACTION} from '../../../../shared/constants/page-actions/page-actions.constants';
import {
  type Alert,
  type AlertFilter,
  type AlertSeverity,
  type AlertType,
} from '../../models/alert/alert.model';
import {AlertCaptionPipe} from '../../pipes/alert-caption.pipe';
import {AlertMessagePipe} from '../../pipes/alert-message.pipe';
import {AlertsStore} from '../../store/alerts/alerts.store';
import {AlertDestinationUtils} from '../../utils/alert-destination.utils';
import {AlertTypeUtils} from '../../utils/alert-type.utils';
import {ALERT_PAGE_SIZE_OPTIONS} from './alerts.constants';

function severityFor(severity: AlertSeverity): AlertItemSeverity {
  switch (severity) {
    case 'Error':
      return 'error';
    case 'Warning':
      return 'warning';
    case 'Info':
      return 'info';
  }
}

@Component({
  selector: 'fns-alerts',
  imports: [
    PageContainerComponent,
    AlertCaptionPipe,
    AlertItemComponent,
    AlertMessagePipe,
    ButtonComponent,
    EmptyStateComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  host: {class: 'block h-full'},
  templateUrl: './alerts.component.html',
})
export class AlertsComponent {
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  public readonly store = inject(AlertsStore);

  public readonly alertsSubtitle = computed(() => {
    const count = this.store.unreadCount();
    if (count > 0) {
      return `${count} unread ${count === 1 ? 'alert' : 'alerts'}`;
    }
    return 'All caught up';
  });

  public readonly emptyTitle = computed(() =>
    this.store.filter() === 'all' ? 'No alerts' : `No ${this.store.filter()} alerts`
  );

  public readonly emptySubtext = computed(() =>
    this.store.filter() === 'all' ? 'All your accounts are healthy.' : 'Try a different filter.'
  );

  // lk-segmented reads `value` as a string, so the options carry the filter ids and the page sizes
  // as strings; the change handlers convert back.
  public readonly filterOptions = computed<{value: AlertFilter; label: string}[]>(() => [
    {value: 'all', label: 'All'},
    {
      value: 'unread',
      label: this.store.unreadCount() > 0 ? `Unread (${this.store.unreadCount()})` : 'Unread',
    },
    {value: 'error', label: 'Errors'},
    {value: 'warning', label: 'Warnings'},
    {value: 'info', label: 'Info'},
  ]);

  public readonly pageSizeOptions = ALERT_PAGE_SIZE_OPTIONS.map(size => ({
    value: String(size),
    label: `${size} per page`,
  }));

  // Everything fits on one page of the smallest size → no pager to show.
  public readonly showPager = computed(
    () => this.store.totalCount() > Math.min(...ALERT_PAGE_SIZE_OPTIONS)
  );

  constructor() {
    inject(CmnPageActionsService)
      .on(ALERTS_MARK_ALL_READ_ACTION)
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.markAllRead());
  }

  public onFilterChange(event: Event): void {
    const {value} = (event as CustomEvent<{value: AlertFilter}>).detail;
    this.store.setFilter(value);
  }

  public onPageSizeChange(event: Event): void {
    const {value} = (event as CustomEvent<{value: string}>).detail;
    this.store.setPageSize(Number(value));
  }

  public iconFor(type: AlertType): LucideIconName {
    return AlertTypeUtils.meta(type).icon;
  }

  public typeLabel(type: AlertType): string {
    return AlertTypeUtils.meta(type).label;
  }

  public severityFor(severity: AlertSeverity): AlertItemSeverity {
    return severityFor(severity);
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

  public onRowKey(event: Event, item: Alert): void {
    if (event.target !== event.currentTarget) {
      return;
    }
    event.preventDefault();
    this.openAlert(item);
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
