import {computed, inject, Injectable, Injector} from '@angular/core';
import {
  ACCOUNT_SIGN_OUT_ID,
  accountMenu,
  type AppLayoutAccount,
  CmnWhatsNewService,
  type NavItem,
  WHATS_NEW_MENU_ID,
  whatsNewMenuItem,
} from '@lifekit-hq/ui';

import {AlertsStore} from '../../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../../modules/auth/store/auth.store';
import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {
  REPORT_PROBLEM_ACTION_ID,
  REPORT_PROBLEM_LABEL,
} from '../../report-problem/constants/report-problem.constants';
import {ReportProblemPanelService} from '../../report-problem/services/report-problem-panel.service';
import {NAV_ITEMS} from '../app-shell.constants';
import {NavUtils} from '../utils/nav.utils';

/**
 * What the shell chrome shows and does, shared by the layout wrapper and the phone More page so the
 * two present the same nav, the same account and the same actions: the permitted nav items (Alerts
 * with its unread count), the signed-in account with its menu, and the handling of a selected item.
 */
@Injectable({providedIn: 'root'})
export class ShellChromeService {
  private readonly authStore = inject(AuthStore);
  private readonly injector = inject(Injector);
  private readonly whatsNew = inject(CmnWhatsNewService);
  private readonly reportProblemPanel = inject(ReportProblemPanelService);

  private readonly allNavItems: NavItem[] = NAV_ITEMS.map(item =>
    (item.route as AppRoute) === AppRoute.Alerts
      ? {
          ...item,
          badge: (): number =>
            this.authStore.sessionResolved() && this.authStore.isAuthenticated()
              ? this.injector.get(AlertsStore).unreadCount()
              : 0,
        }
      : item
  );

  public readonly navItems = computed(() =>
    this.allNavItems.filter(item => this.isPermitted(item.route))
  );

  public readonly account = computed<AppLayoutAccount>(() => ({
    label: this.authStore.avatarInitials(),
    name: this.authStore.fullName() || undefined,
    email: this.authStore.email() ?? undefined,
    menuItems: accountMenu({
      settingsRoute: AppRoute.Settings,
      extras: [
        whatsNewMenuItem(this.whatsNew.hasUnread()),
        {id: REPORT_PROBLEM_ACTION_ID, label: REPORT_PROBLEM_LABEL, icon: 'MessageSquareWarning'},
      ],
    }),
  }));

  public isPermitted(entry: string): boolean {
    return NavUtils.isPermitted(entry, this.authStore.permissions());
  }

  /**
   * Handles an account item or palette action the app owns: Sign out, What's new, Report a problem.
   * Returns false for any other id, for the caller to handle.
   */
  public handle(id: string): boolean {
    switch (id) {
      case ACCOUNT_SIGN_OUT_ID:
        this.authStore.logout();
        return true;
      case WHATS_NEW_MENU_ID:
        this.whatsNew.open();
        return true;
      case REPORT_PROBLEM_ACTION_ID:
        this.reportProblemPanel.open();
        return true;
      default:
        return false;
    }
  }
}
