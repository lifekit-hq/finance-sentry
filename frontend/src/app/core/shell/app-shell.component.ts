import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {Router, RouterOutlet} from '@angular/router';
import {
  AppLayoutAccount,
  AppLayoutComponent,
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
import {CONNECT_ACTION_ID, PERMISSION_BY_ENTRY, PHONE_TAB_ROUTES} from './app-shell.constants';

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
  {id: PALETTE_THEME_ACTION, label: 'Toggle Dark Mode', icon: 'Moon', group: 'Actions'},
  {id: '_logout', label: 'Sign Out', icon: 'LogOut', group: 'Actions'},
];

const AVATAR_MENU_ITEMS: MenuItem[] = [
  {id: '/settings', label: 'Settings', icon: 'Settings2'},
  {id: '_logout', label: 'Log out', icon: 'LogOut', destructive: true},
];

@Component({
  selector: 'fns-app-shell',
  imports: [AppLayoutComponent, RouterOutlet, ChatWidgetComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <cmn-app-layout
      [navItems]="navItems()"
      [paletteItems]="paletteItems()"
      [account]="account()"
      [showThemeToggle]="false"
      [versionLabel]="versionLabel"
      [tabRoutes]="tabRoutes"
      [phoneOverlay]="true"
      (navClick)="navigate($event)"
      (paletteAction)="handlePaletteAction($event)"
      (avatarMenuSelect)="handleAvatarMenuSelect($event)"
      brand="Finance Sentry"
    >
      <router-outlet />
    </cmn-app-layout>

    @if (canUseAi()) {
      <fns-chat-widget />
    }
  `,
})
export class AppShellComponent {
  private readonly router = inject(Router);
  private readonly authStore = inject(AuthStore);
  private readonly alertsStore = inject(AlertsStore);
  private readonly allNavItems: NavItem[] = [
    {label: 'Home', icon: 'LayoutDashboard', route: AppRoute.Dashboard},
    {label: 'Accounts', icon: 'Building2', route: AppRoute.Accounts},
    {label: 'Transactions', icon: 'ArrowLeftRight', route: AppRoute.Transactions},
    {label: 'Budgets', icon: 'Zap', route: AppRoute.Budgets},
    {label: 'Subscriptions', icon: 'RefreshCw', route: AppRoute.Subscriptions},
    {
      label: 'Alerts',
      icon: 'Bell',
      route: AppRoute.Alerts,
      badge: () => this.alertsStore.unreadCount(),
    },
    {label: 'Events', icon: 'CalendarDays', route: AppRoute.Events},
    {label: 'Ledger', icon: 'Sparkles', route: AppRoute.Ledger},
    {label: 'Settings', icon: 'Settings2', route: AppRoute.Settings},
  ];

  public readonly canUseAi = this.authStore.canUseAi;
  public readonly versionLabel = `v${APP_VERSION}`;
  public readonly tabRoutes = [...PHONE_TAB_ROUTES];
  public readonly navItems = computed(() =>
    this.allNavItems.filter(item => this.isPermitted(item.route))
  );
  public readonly paletteItems = computed(() =>
    PALETTE_ITEMS.filter(item => this.isPermitted(item.id))
  );
  public readonly account = computed<AppLayoutAccount>(() => ({
    label: this.authStore.avatarInitials(),
    menuItems: AVATAR_MENU_ITEMS,
  }));

  public navigate(item: NavItem): void {
    void this.router.navigateByUrl(item.route);
  }

  public handleAvatarMenuSelect(item: MenuItem): void {
    if (item.id === '/settings') {
      void this.router.navigateByUrl(AppRoute.Settings);
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
    }
  }

  private isPermitted(entry: string): boolean {
    const permission = PERMISSION_BY_ENTRY[entry];
    return permission === undefined || this.authStore.permissions().includes(permission);
  }
}
