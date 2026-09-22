import {ChangeDetectionStrategy, Component, computed, HostListener, inject} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {NavigationEnd, Router, RouterOutlet} from '@angular/router';
import {
  AppLayoutComponent,
  CmnDialogBareContainerComponent,
  CmnDialogService,
  CommandPaletteComponent,
  type CommandPaletteItem,
  type MenuItem,
  type NavItem,
  type PaletteResult,
  ThemeService,
} from '@lifekit-hq/ui';
import {filter, map} from 'rxjs';

import {ChatWidgetComponent} from '../../modules/agent/components/chat-widget/chat-widget.component';
import {AlertsStore} from '../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {APP_VERSION} from '../../shared/constants/version/version.constants';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';

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
  {id: '_connect', label: 'Connect Account', icon: 'Link', group: 'Actions'},
  {id: '_theme', label: 'Toggle Dark Mode', icon: 'Moon', group: 'Actions'},
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
      [navItems]="navItems"
      [activeRoute]="activeRoute()"
      [isDark]="isDark()"
      [avatarMenuItems]="avatarMenuItems"
      [versionLabel]="versionLabel"
      (navClick)="navigate($event)"
      (themeToggle)="themeService.toggle()"
      (searchClick)="openPalette()"
      (avatarMenuSelect)="handleAvatarMenuSelect($event)"
      avatarLabel="D"
    >
      <router-outlet />
    </cmn-app-layout>

    <fns-chat-widget />
  `,
})
export class AppShellComponent {
  private readonly router = inject(Router);
  private readonly authStore = inject(AuthStore);
  private readonly dialog = inject(CmnDialogService);
  private readonly theme = toSignal(inject(ThemeService).activeTheme$, {
    initialValue: 'light' as const,
  });
  private readonly routerUrl = toSignal(
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      map(e => e.urlAfterRedirects)
    ),
    {initialValue: this.router.url}
  );

  private readonly alertsStore = inject(AlertsStore);

  public readonly themeService = inject(ThemeService);
  public readonly avatarMenuItems: MenuItem[] = AVATAR_MENU_ITEMS;
  public readonly versionLabel = `v${APP_VERSION}`;
  public readonly navItems: NavItem[] = [
    {label: 'Dashboard', icon: 'LayoutDashboard', route: AppRoute.Dashboard},
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
  public readonly isDark = computed(() => this.theme() === 'dark');
  public readonly activeRoute = computed(() => {
    const url = this.routerUrl();
    const match = this.navItems.find(item => url.startsWith(item.route));
    return match?.route ?? '';
  });

  @HostListener('window:keydown', ['$event'])
  public onGlobalKeyDown(e: KeyboardEvent): void {
    if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
      e.preventDefault();
      this.openPalette();
    }
  }

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

  public openPalette(): void {
    this.dialog
      .open<PaletteResult>(CommandPaletteComponent, {
        data: PALETTE_ITEMS,
        container: CmnDialogBareContainerComponent,
        hasBackdrop: false,
        panelClass: [],
        autoFocus: false,
        disableClose: true,
      })
      .afterClosed()
      .subscribe(result => {
        if (!result) {
          return;
        }
        if (result.type === 'navigate') {
          void this.router.navigateByUrl(result.id);
        } else {
          this.handleAction(result.id);
        }
      });
  }

  private handleAction(id: string): void {
    if (id === '_theme') {
      this.themeService.toggle();
    } else if (id === '_logout') {
      this.authStore.logout();
    } else if (id === '_connect') {
      void this.router.navigateByUrl(AppRoute.AccountsList);
    }
  }
}
