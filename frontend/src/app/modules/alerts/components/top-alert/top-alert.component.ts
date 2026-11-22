import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router, RouterLink} from '@angular/router';
import {AlertItemComponent} from '@lifekit-hq/ui';
import {type Alert} from '@shared/models/alert/alert.model';
import {AlertDestinationUtils} from '@shared/utils/alert-destination.utils';
import {AlertTypeUtils} from '@shared/utils/alert-type.utils';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AlertCaptionPipe} from '../../pipes/alert-caption.pipe';
import {AlertMessagePipe} from '../../pipes/alert-message.pipe';
import {AlertsStore} from '../../store/alerts/alerts.store';
import {TopAlertStore} from '../../store/top-alert/top-alert.store';
import {AlertSeverityUtils} from '../../utils/alert-severity.utils';

/**
 * Home's insight: the newest unread alert, opened the way the Alerts page opens it, and a link to the
 * rest. Renders nothing while everything is read or when Alerts cannot be reached.
 */
@Component({
  selector: 'fns-top-alert',
  imports: [AlertCaptionPipe, AlertItemComponent, AlertMessagePipe, RouterLink],
  templateUrl: './top-alert.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [TopAlertStore],
})
export class TopAlertComponent {
  private readonly router = inject(Router);
  private readonly alerts = inject(AlertsStore);

  public readonly store = inject(TopAlertStore);
  public readonly alertsRoute = AppRoute.Alerts;
  public readonly row = computed(() => {
    const alert = this.store.alert();
    if (!alert) {
      return null;
    }
    const meta = AlertTypeUtils.meta(alert.type);
    return {
      alert,
      icon: meta.icon,
      badgeLabel: meta.label,
      severity: AlertSeverityUtils.toItemSeverity(alert.severity),
    };
  });

  public open(item: Alert): void {
    this.alerts.markRead(item.id);
    const navigation = AlertDestinationUtils.resolve(item);
    if (navigation?.kind === 'external') {
      window.open(navigation.url, '_blank', 'noopener,noreferrer');
    } else if (navigation?.kind === 'url') {
      void this.router.navigateByUrl(navigation.url);
    } else if (navigation) {
      void this.router.navigate(navigation.commands);
    } else {
      void this.router.navigate([this.alertsRoute]);
    }
  }
}
