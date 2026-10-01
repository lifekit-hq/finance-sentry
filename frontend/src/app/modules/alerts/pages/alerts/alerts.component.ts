import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router} from '@angular/router';
import {
  AlertItemComponent,
  type AlertItemSeverity,
  ChipComponent,
  EmptyStateComponent,
  type LucideIconName,
  PageHeaderComponent,
  ToastService,
} from '@lifekit-hq/ui';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {
  ALERT_TYPE_META_REGISTRY,
  type AlertTypeMeta,
  DEFAULT_ALERT_TYPE_META,
} from '../../constants/alert-type-meta.constants';
import {
  type Alert,
  type AlertFilter,
  type AlertSeverity,
  type AlertType,
} from '../../models/alert/alert.model';
import {AlertMessagePipe} from '../../pipes/alert-message.pipe';
import {AlertsStore} from '../../store/alerts/alerts.store';

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
    AlertItemComponent,
    AlertMessagePipe,
    ChipComponent,
    EmptyStateComponent,
    PageHeaderComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
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

  public readonly filtered = computed(() => {
    const f = this.store.filter();
    const all = this.store.alerts();
    if (f === 'unread') {
      return all.filter(a => !a.isRead);
    }
    if (f === 'error') {
      return all.filter(a => a.severity === 'Error');
    }
    if (f === 'warning') {
      return all.filter(a => a.severity === 'Warning');
    }
    if (f === 'info') {
      return all.filter(a => a.severity === 'Info');
    }
    return all;
  });

  public readonly filterOptions: {id: AlertFilter; label: () => string}[] = [
    {id: 'all', label: () => 'All'},
    {
      id: 'unread',
      label: () =>
        this.store.unreadCount() > 0 ? `Unread (${this.store.unreadCount()})` : 'Unread',
    },
    {id: 'error', label: () => 'Errors'},
    {id: 'warning', label: () => 'Warnings'},
    {id: 'info', label: () => 'Info'},
  ];

  public iconFor(type: AlertType): LucideIconName {
    return this.metaFor(type).icon;
  }

  public typeLabel(type: AlertType): string {
    return this.metaFor(type).label;
  }

  // Tolerate alert types the backend added before this registry did (e.g. PolicyViolation,
  // Opportunity) — an unmapped type must not crash the whole alerts page.
  private metaFor(type: AlertType): AlertTypeMeta {
    const registry = ALERT_TYPE_META_REGISTRY as Record<string, AlertTypeMeta | undefined>;
    return registry[type] ?? DEFAULT_ALERT_TYPE_META;
  }

  public severityFor(severity: AlertSeverity): AlertItemSeverity {
    return severityFor(severity);
  }

  public markAllRead(): void {
    this.store.markAllRead();
    this.toast.show('All alerts marked as read', 'success');
  }

  public dismiss(id: string): void {
    this.store.dismiss(id);
    this.toast.show('Alert dismissed', 'info');
  }

  public openAlert(alert: Alert): void {
    if (!alert.isRead) {
      this.store.markRead(alert.id);
    }
    const target = alert.type === 'UnusualSpend' ? AppRoute.Transactions : AppRoute.AccountsList;
    void this.router.navigateByUrl(target);
  }
}
