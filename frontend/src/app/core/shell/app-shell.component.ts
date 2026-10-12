import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router, RouterOutlet} from '@angular/router';
import {
  AppLayoutAccount,
  AppLayoutComponent,
  CmnShellService,
  CommandPaletteItem,
  type MenuItem,
  type NavItem,
  PALETTE_THEME_ACTION,
} from '@lifekit-hq/ui';

import {ChatWidgetComponent} from '../../modules/agent/components/chat-widget/chat-widget.component';
import {AlertsStore} from '../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {APP_VERSION} from '../../shared/constants/version/version.constants';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {
  REPORT_PROBLEM_ACTION_ID,
  REPORT_PROBLEM_LABEL,
} from '../report-problem/constants/report-problem.constants';
import {ReportProblemPanelService} from '../report-problem/services/report-problem-panel.service';
import {WHATS_NEW_ACTION_ID, WHATS_NEW_LABEL} from '../whats-new/constants/whats-new.constants';
import {WhatsNewPanelService} from '../whats-new/services/whats-new-panel.service';
import {WhatsNewStore} from '../whats-new/store/whats-new.store';
import {CONNECT_ACTION_ID, NAV_ITEMS, PHONE_TAB_ROUTES} from './app-shell.constants';
import {PaletteEntitiesStore} from './store/palette-entities.store';
import {NavUtils} from './utils/nav.utils';

const PALETTE_ITEMS: CommandPaletteItem[] = [
  {id: AppRoute.Dashboard, label: 'Dashboard', icon: 'LayoutDashboard', group: 'Pages'},
  {id: AppRoute.AccountsList, label: 'Accounts', icon: 'Building2', group: 'Pages'},
  {id: AppRoute.Transactions, label: 'Transactions', icon: 'ArrowLeftRight', group: 'Pages'},
  {
    id: AppRoute.AccountsInvestments,
    label: 'Investments',
    icon: 'ChartNoAxesColumn',
    group: 'Pages',
  },
  {id: AppRoute.Budgets, label: 'Budgets', icon: 'Zap', group: 'Pages'},
  {id: AppRoute.Subscriptions, label: 'Subscriptions', icon: 'RefreshCw', group: 'Pages'},
  {id: AppRoute.Alerts, label: 'Alerts', icon: 'Bell', group: 'Pages'},
  {id: AppRoute.Events, label: 'Events', icon: 'CalendarDays', group: 'Pages'},
  {id: AppRoute.Ledger, label: 'Ledger', icon: 'Sparkles', group: 'Pages'},
  {id: AppRoute.Settings, label: 'Settings', icon: 'Settings2', group: 'Pages'},
  {id: AppRoute.SettingsPeople, label: 'People', icon: 'Users', group: 'Pages'},
  {id: CONNECT_ACTION_ID, label: 'Connect Account', icon: 'Link', group: 'Actions'},
  {id: WHATS_NEW_ACTION_ID, label: WHATS_NEW_LABEL, icon: 'Megaphone', group: 'Actions'},
  {
    id: REPORT_PROBLEM_ACTION_ID,
    label: REPORT_PROBLEM_LABEL,
    icon: 'MessageSquareWarning',
    group: 'Actions',
  },
  {id: PALETTE_THEME_ACTION, label: 'Toggle Dark Mode', icon: 'Moon', group: 'Actions'},
  {id: '_logout', label: 'Sign Out', icon: 'LogOut', group: 'Actions'},
];

const AVATAR_MENU_ITEMS: MenuItem[] = [
  {id: '/settings', label: 'Settings', icon: 'Settings2'},
  {id: WHATS_NEW_ACTION_ID, label: WHATS_NEW_LABEL, icon: 'Megaphone'},
  {id: REPORT_PROBLEM_ACTION_ID, label: REPORT_PROBLEM_LABEL, icon: 'MessageSquareWarning'},
  {id: '_logout', label: 'Log out', icon: 'LogOut', destructive: true},
];

@Component({
  selector: 'fns-app-shell',
  imports: [AppLayoutComponent, RouterOutlet, ChatWidgetComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [PaletteEntitiesStore],
  template: `
    <cmn-app-layout
      [navItems]="navItems()"
      [paletteItems]="paletteItems()"
      [account]="account()"
      [showThemeToggle]="false"
      [versionLabel]="versionLabel"
      [versionDot]="hasUnreadNotes()"
      [tabRoutes]="tabRoutes"
      [moreRoute]="moreRoute"
      [phoneOverlay]="true"
      (navClick)="navigate($event)"
      (versionClick)="openWhatsNew()"
      (paletteAction)="handlePaletteAction($event)"
      (avatarMenuSelect)="handleAvatarMenuSelect($event)"
      brand="Finance Sentry"
    >
      <router-outlet />
      @if (canUseAi()) {
        <fns-chat-widget cmnAppLayoutFab />
      }
    </cmn-app-layout>
  `,
})
export class AppShellComponent {
  private readonly router = inject(Router);
  private readonly shell = inject(CmnShellService);
  private readonly authStore = inject(AuthStore);
  private readonly alertsStore = inject(AlertsStore);
  private readonly whatsNewPanel = inject(WhatsNewPanelService);
  private readonly reportProblemPanel = inject(ReportProblemPanelService);
  private readonly paletteEntities = inject(PaletteEntitiesStore);
  private readonly allNavItems: NavItem[] = NAV_ITEMS.map(item =>
    (item.route as AppRoute) === AppRoute.Alerts
      ? {...item, badge: (): number => this.alertsStore.unreadCount()}
      : item
  );

  public readonly canUseAi = this.authStore.canUseAi;
  public readonly versionLabel = `v${APP_VERSION} · ${WHATS_NEW_LABEL}`;
  public readonly hasUnreadNotes = inject(WhatsNewStore).hasUnread;
  public readonly tabRoutes = [...PHONE_TAB_ROUTES];
  public readonly moreRoute = AppRoute.More;
  public readonly navItems = computed(() =>
    this.allNavItems.filter(item => this.isPermitted(item.route))
  );
  public readonly paletteItems = computed(() =>
    [...PALETTE_ITEMS, ...this.paletteEntities.items()].filter(item => this.isPermitted(item.id))
  );
  public readonly account = computed<AppLayoutAccount>(() => ({
    label: this.authStore.avatarInitials(),
    menuItems: AVATAR_MENU_ITEMS,
  }));

  /** The rail and the sidebar leave navigating to the app; the phone tab bar navigates itself. */
  public navigate(item: NavItem): void {
    if (!this.shell.isPhone()) {
      void this.router.navigateByUrl(item.route);
    }
  }

  public openWhatsNew(): void {
    this.whatsNewPanel.open();
  }

  public handleAvatarMenuSelect(item: MenuItem): void {
    if (item.id === '/settings') {
      void this.router.navigateByUrl(AppRoute.Settings);
      return;
    }
    if (item.id === WHATS_NEW_ACTION_ID) {
      this.openWhatsNew();
      return;
    }
    if (item.id === REPORT_PROBLEM_ACTION_ID) {
      this.reportProblemPanel.open();
      return;
    }
    if (item.id === '_logout') {
      this.authStore.logout();
    }
  }

  public handlePaletteAction(id: string): void {
    if (id === '_logout') {
      this.authStore.logout();
    } else if (id === CONNECT_ACTION_ID) {
      void this.router.navigateByUrl(AppRoute.AccountsList);
    } else if (id === WHATS_NEW_ACTION_ID) {
      this.openWhatsNew();
    } else if (id === REPORT_PROBLEM_ACTION_ID) {
      this.reportProblemPanel.open();
    }
  }

  private isPermitted(entry: string): boolean {
    return NavUtils.isPermitted(entry, this.authStore.permissions());
  }
}
