import {ChangeDetectionStrategy, Component, signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {AppUpdateService} from '@lifekit-hq/core/pwa';
import {
  ACCOUNT_SIGN_OUT_ID,
  APPEARANCE_PALETTE_ITEMS,
  CmnPullRefreshService,
  CmnShellService,
  CmnWhatsNewService,
  type MenuItem,
} from '@lifekit-hq/ui';
import {of} from 'rxjs';

import {ChatWidgetComponent} from '../../modules/agent/components/chat-widget/chat-widget.component';
import {AlertsStore} from '../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {APP_VERSION} from '../../shared/constants/version/version.constants';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {ReportProblemPanelService} from '../report-problem/services/report-problem-panel.service';
import {AppShellComponent} from './app-shell.component';
import {PaletteEntitiesService} from './services/palette-entities.service';
import {ShellChromeService} from './services/shell-chrome.service';

@Component({
  selector: 'fns-chat-widget',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class ChatWidgetStubComponent {}

describe('AppShellComponent FAB clearance', () => {
  const openWhatsNew = vi.fn();
  const openReport = vi.fn();
  const logout = vi.fn();
  const hasUnread = signal(false);
  const updateReady = signal(false);
  const reload = vi.fn().mockResolvedValue(undefined);
  const refresh = vi.fn().mockResolvedValue(undefined);
  const refreshAvailable = signal(false);
  const sessionResolved = signal(true);
  const isAuthenticated = signal(true);
  const paletteLoad = vi.fn();
  const unreadCount = vi.fn(() => 3);

  beforeEach(() => {
    openWhatsNew.mockClear();
    openReport.mockClear();
    logout.mockClear();
    hasUnread.set(false);
    updateReady.set(false);
    reload.mockClear();
    refresh.mockClear();
    refreshAvailable.set(false);
    sessionResolved.set(true);
    isAuthenticated.set(true);
    paletteLoad.mockClear();
    unreadCount.mockClear();
  });
  const canUseAi = signal(true);

  const setup = async (url: string, wide = false): Promise<ComponentFixture<AppShellComponent>> => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: CmnShellService,
          useValue: {mode: signal(wide ? 'sidebar' : 'phone'), isPhone: signal(!wide)},
        },
        provideRouter([{path: '**', children: []}]),
        {
          provide: AuthStore,
          useValue: {
            canUseAi,
            sessionResolved,
            isAuthenticated,
            permissions: signal([]),
            avatarInitials: signal('DT'),
            fullName: signal('Denys Tester'),
            email: signal('denys@test.dev'),
            logout,
          },
        },
        {provide: AppUpdateService, useValue: {updateReady, reload}},
        {provide: CmnPullRefreshService, useValue: {available: refreshAvailable, refresh}},
        {
          provide: CmnWhatsNewService,
          useValue: {
            open: openWhatsNew,
            hasUnread,
            version: APP_VERSION,
            versionLabel: `v${APP_VERSION} · What's new`,
          },
        },
        {provide: ReportProblemPanelService, useValue: {open: openReport}},
        {provide: AlertsStore, useValue: {unreadCount}},
        {
          provide: PaletteEntitiesService,
          useValue: {
            load: () => {
              paletteLoad();
              return of({
                holdings: [{symbol: 'AAPL', assetClass: 'equity'}],
                watchlist: [{ticker: 'NVDA'}],
                accounts: [{accountId: 'a1', bankName: 'Monobank', accountNumberLast4: '1234'}],
              });
            },
          },
        },
      ],
    });
    TestBed.overrideComponent(AppShellComponent, {
      remove: {imports: [ChatWidgetComponent]},
      add: {imports: [ChatWidgetStubComponent]},
    });
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    return fixture;
  };

  it('renders the offline, update and install prompts in the layout notice slot', async () => {
    const fixture = await setup('/');
    const host = fixture.nativeElement as HTMLElement;
    // The slot's container is `empty:hidden`: a wrapper element with a flex gap would keep it
    // visible and push every page down, so the prompts must be its direct children.
    const notice = host.querySelector('lk-offline-banner')?.parentElement;

    expect(notice?.querySelector(':scope > lk-offline-banner')).not.toBeNull();
    expect(notice?.querySelector(':scope > lk-update-prompt')).not.toBeNull();
    expect(notice?.querySelector(':scope > lk-install-hint')).not.toBeNull();
  });

  it('shows the update prompt once a version is downloaded and reloads on request', async () => {
    updateReady.set(true);
    const fixture = await setup('/');
    const prompt = (fixture.nativeElement as HTMLElement).querySelector(
      'lk-update-prompt'
    ) as HTMLElement & {ready: boolean};

    expect(prompt.ready).toBe(true);

    prompt.dispatchEvent(new CustomEvent('lk-update-prompt-reload'));

    expect(reload).toHaveBeenCalledOnce();
  });

  it('adds held, watchlist and account entities to the palette', async () => {
    const fixture = await setup(AppRoute.Settings);
    const ids = fixture.componentInstance.paletteItems().map(item => item.id);
    expect(ids).toEqual(
      expect.arrayContaining(['/assets/AAPL', '/assets/NVDA', '/transactions?account=a1'])
    );
  });

  it('loads no palette entities for a visitor without a session', async () => {
    isAuthenticated.set(false);
    const fixture = await setup(AppRoute.Settings);
    const ids = fixture.componentInstance.paletteItems().map(item => item.id);

    expect(paletteLoad).not.toHaveBeenCalled();
    expect(ids).not.toContain('/assets/AAPL');
  });

  it('draws the shell while the session call is out and holds the routed content back', async () => {
    sessionResolved.set(false);
    const fixture = await setup(AppRoute.Settings);
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelector('cmn-app-layout')).not.toBeNull();
    expect(host.querySelector('router-outlet')).toBeNull();

    sessionResolved.set(true);
    fixture.detectChanges();

    expect(host.querySelector('router-outlet')).not.toBeNull();
  });

  it('leaves the Alerts badge at zero without touching the store until a session is signed in', async () => {
    isAuthenticated.set(false);
    const fixture = await setup(AppRoute.Settings);
    const alerts = TestBed.inject(ShellChromeService)
      .navItems()
      .find(item => (item.route as AppRoute) === AppRoute.Alerts);

    expect(alerts?.badge?.()).toBe(0);
    expect(unreadCount).not.toHaveBeenCalled();

    isAuthenticated.set(true);
    fixture.detectChanges();

    expect(alerts?.badge?.()).toBe(3);
  });

  it('shows the store avatar initials in the top bar', async () => {
    const fixture = await setup(AppRoute.Settings);
    expect(fixture.componentInstance.account().label).toBe('DT');
  });

  it('names the person in the account header', async () => {
    const account = (await setup(AppRoute.Settings)).componentInstance.account();

    expect(account.name).toBe('Denys Tester');
    expect(account.email).toBe('denys@test.dev');
  });

  it('builds the account menu as the standard has it: Settings, extras, Sign out', async () => {
    const menu = (await setup(AppRoute.Settings)).componentInstance.account().menuItems;

    expect(menu.map(item => item.label)).toEqual([
      'Settings',
      "What's new",
      'Report a problem',
      'Sign out',
    ]);
    expect(menu[0].id).toBe(AppRoute.Settings);
    expect(menu[3].id).toBe(ACCOUNT_SIGN_OUT_ID);
  });

  it('reaches Settings from the account menu, not the nav', async () => {
    const fixture = await setup(AppRoute.Dashboard, true);

    expect(fixture.componentInstance.navItems().map(item => item.route)).not.toContain(
      AppRoute.Settings
    );
  });

  it('offers System, Light and Dark in the palette instead of a toggle', async () => {
    const ids = (await setup(AppRoute.Dashboard, true)).componentInstance
      .paletteItems()
      .map(item => item.id);

    expect(ids).toEqual(expect.arrayContaining(APPEARANCE_PALETTE_ITEMS.map(item => item.id)));
    expect(ids).not.toContain('_theme');
  });

  it('signs out from the avatar menu', async () => {
    const fixture = await setup(AppRoute.Settings, true);
    fixture.componentInstance.handleAvatarMenuSelect({
      id: ACCOUNT_SIGN_OUT_ID,
      label: 'Sign out',
    });

    expect(logout).toHaveBeenCalledOnce();
  });

  it('signs out from the command palette', async () => {
    const fixture = await setup(AppRoute.Settings, true);
    const entry = fixture.componentInstance.paletteItems().find(item => item.label === 'Sign out');

    expect(entry?.id).toBe(ACCOUNT_SIGN_OUT_ID);
    fixture.componentInstance.handlePaletteAction(entry?.id ?? '');
    expect(logout).toHaveBeenCalledOnce();
  });

  it('navigates on a sidebar nav click', async () => {
    const fixture = await setup(AppRoute.Settings, true);
    const router = TestBed.inject(Router);
    fixture.componentInstance.navigate({label: 'Budgets', icon: 'Zap', route: AppRoute.Budgets});
    await fixture.whenStable();
    expect(router.url).toBe(AppRoute.Budgets);
  });

  it('leaves a phone tab click to the shell, which navigates itself', async () => {
    const fixture = await setup(AppRoute.Settings);
    const router = TestBed.inject(Router);
    fixture.componentInstance.navigate({label: 'Budgets', icon: 'Zap', route: AppRoute.Budgets});
    await fixture.whenStable();
    expect(router.url).toBe(AppRoute.Settings);
  });

  it("opens the what's new panel from the avatar menu", async () => {
    const fixture = await setup(AppRoute.Settings, true);
    const item = fixture.componentInstance
      .account()
      .menuItems.find(entry => entry.label === "What's new");
    expect(item).toBeDefined();
    fixture.componentInstance.handleAvatarMenuSelect(item as MenuItem);
    expect(openWhatsNew).toHaveBeenCalledOnce();
  });

  it("puts the unread dot on the account menu's What's new item", async () => {
    const fixture = await setup(AppRoute.Settings, true);
    const dot = (): boolean | undefined =>
      fixture.componentInstance.account().menuItems?.find(entry => entry.label === "What's new")
        ?.dot;
    expect(dot()).toBe(false);

    hasUnread.set(true);
    expect(dot()).toBe(true);
  });

  it("opens the what's new panel from the command palette", async () => {
    const fixture = await setup(AppRoute.Settings, true);
    const entry = fixture.componentInstance
      .paletteItems()
      .find(item => item.label === "What's new");
    expect(entry).toBeDefined();
    fixture.componentInstance.handlePaletteAction(entry?.id ?? '');
    expect(openWhatsNew).toHaveBeenCalledOnce();
  });

  it('opens the report sheet from the avatar menu', async () => {
    const fixture = await setup(AppRoute.Settings, true);
    const item = fixture.componentInstance
      .account()
      .menuItems.find(entry => entry.label === 'Report a problem');
    expect(item).toBeDefined();
    fixture.componentInstance.handleAvatarMenuSelect(item as MenuItem);
    expect(openReport).toHaveBeenCalledOnce();
  });

  it('opens the report sheet from the command palette', async () => {
    const fixture = await setup(AppRoute.Settings, true);
    const entry = fixture.componentInstance
      .paletteItems()
      .find(item => item.label === 'Report a problem');
    expect(entry).toBeDefined();
    fixture.componentInstance.handlePaletteAction(entry?.id ?? '');
    expect(openReport).toHaveBeenCalledOnce();
  });

  it('offers a palette refresh only while the page on screen registered a reload', async () => {
    const fixture = await setup(AppRoute.Dashboard, true);
    const find = () =>
      fixture.componentInstance.paletteItems().find(item => item.label === 'Refresh this page');
    expect(find()).toBeUndefined();

    refreshAvailable.set(true);
    const entry = find();
    expect(entry).toBeDefined();
    fixture.componentInstance.handlePaletteAction(entry?.id ?? '');
    expect(refresh).toHaveBeenCalledOnce();
  });

  describe('sidebar version footer', () => {
    const footer = (fixture: ComponentFixture<AppShellComponent>): HTMLButtonElement | null =>
      (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
        'aside button[type="button"].border-t'
      );

    it("is a button labelled with the version and What's new that opens the panel", async () => {
      const fixture = await setup(AppRoute.Settings, true);
      const button = footer(fixture);

      expect(button?.textContent).toContain(`v${APP_VERSION} · What's new`);
      button?.click();
      expect(openWhatsNew).toHaveBeenCalledOnce();
    });

    it('shows the unread dot only while the release is unread', async () => {
      const fixture = await setup(AppRoute.Settings, true);
      expect(footer(fixture)?.querySelector('.cmn-badge-indicator')).toBeNull();

      hasUnread.set(true);
      fixture.detectChanges();
      expect(footer(fixture)?.querySelector('.cmn-badge-indicator')).not.toBeNull();
    });
  });
});
