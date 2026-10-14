import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {Router, RouterOutlet} from '@angular/router';
import {AppUpdateService} from '@lifekit-hq/core/pwa';
import {
  ACCOUNT_SIGN_OUT_ID,
  APPEARANCE_PALETTE_ITEMS,
  AppLayoutComponent,
  CmnShellService,
  CommandPaletteItem,
  type MenuItem,
  type NavItem,
} from '@lifekit-hq/ui';

import {ChatWidgetComponent} from '../../modules/agent/components/chat-widget/chat-widget.component';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {APP_VERSION} from '../../shared/constants/version/version.constants';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {
  REPORT_PROBLEM_ACTION_ID,
  REPORT_PROBLEM_LABEL,
} from '../report-problem/constants/report-problem.constants';
import {WHATS_NEW_ACTION_ID, WHATS_NEW_LABEL} from '../whats-new/constants/whats-new.constants';
import {WhatsNewStore} from '../whats-new/store/whats-new.store';
import {CONNECT_ACTION_ID, PHONE_TAB_ROUTES} from './app-shell.constants';
import {ShellChromeService} from './services/shell-chrome.service';
import {PaletteEntitiesStore} from './store/palette-entities.store';

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
  {id: AppRoute.LedgerDecisions, label: 'Decisions', icon: 'ListChecks', group: 'Pages'},
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
  ...APPEARANCE_PALETTE_ITEMS,
  {id: ACCOUNT_SIGN_OUT_ID, label: 'Sign out', icon: 'LogOut', group: 'Actions'},
];

@Component({
  selector: 'fns-app-shell',
  imports: [AppLayoutComponent, RouterOutlet, ChatWidgetComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [PaletteEntitiesStore],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  template: `
    <cmn-app-layout
      [navItems]="navItems()"
      [paletteItems]="paletteItems()"
      [account]="account()"
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
      <lk-offline-banner cmnAppLayoutNotice />
      <lk-update-prompt
        [ready]="appUpdate.updateReady()"
        (lk-update-prompt-reload)="appUpdate.reload()"
        cmnAppLayoutNotice
      />
      <lk-install-hint cmnAppLayoutNotice />
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
  private readonly chrome = inject(ShellChromeService);
  private readonly paletteEntities = inject(PaletteEntitiesStore);

  public readonly appUpdate = inject(AppUpdateService);

  public readonly canUseAi = inject(AuthStore).canUseAi;
  public readonly versionLabel = `v${APP_VERSION} · ${WHATS_NEW_LABEL}`;
  public readonly hasUnreadNotes = inject(WhatsNewStore).hasUnread;
  public readonly tabRoutes = [...PHONE_TAB_ROUTES];
  public readonly moreRoute = AppRoute.More;
  public readonly navItems = this.chrome.navItems;
  public readonly account = this.chrome.account;
  public readonly paletteItems = computed(() =>
    [...PALETTE_ITEMS, ...this.paletteEntities.items()].filter(item =>
      this.chrome.isPermitted(item.id)
    )
  );

  /** The rail and the sidebar leave navigating to the app; the phone tab bar navigates itself. */
  public navigate(item: NavItem): void {
    if (!this.shell.isPhone()) {
      void this.router.navigateByUrl(item.route);
    }
  }

  public openWhatsNew(): void {
    this.chrome.openWhatsNew();
  }

  /** The layout navigates to Settings itself; the app handles Sign out and its own extras. */
  public handleAvatarMenuSelect(item: MenuItem): void {
    this.chrome.handle(item.id);
  }

  /** The layout navigates to pages and applies the appearance entries itself. */
  public handlePaletteAction(id: string): void {
    if (!this.chrome.handle(id) && id === CONNECT_ACTION_ID) {
      void this.router.navigateByUrl(AppRoute.AccountsList);
    }
  }
}
