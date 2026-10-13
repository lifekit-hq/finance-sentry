import {ChangeDetectionStrategy, Component, signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {
  ACCOUNT_SIGN_OUT_ID,
  APPEARANCE_PALETTE_ITEMS,
  CmnShellService,
  type MenuItem,
} from '@lifekit-hq/ui';
import {of} from 'rxjs';

import {ChatWidgetComponent} from '../../modules/agent/components/chat-widget/chat-widget.component';
import {AlertsStore} from '../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {APP_VERSION} from '../../shared/constants/version/version.constants';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {ReportProblemPanelService} from '../report-problem/services/report-problem-panel.service';
import {WhatsNewPanelService} from '../whats-new/services/whats-new-panel.service';
import {WhatsNewStore} from '../whats-new/store/whats-new.store';
import {AppShellComponent} from './app-shell.component';
import {PaletteEntitiesService} from './services/palette-entities.service';

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

  beforeEach(() => {
    openWhatsNew.mockClear();
    openReport.mockClear();
    logout.mockClear();
    hasUnread.set(false);
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
            permissions: signal([]),
            avatarInitials: signal('DT'),
            fullName: signal('Denys Tester'),
            email: signal('denys@test.dev'),
            logout,
          },
        },
        {provide: WhatsNewPanelService, useValue: {open: openWhatsNew}},
        {provide: WhatsNewStore, useValue: {hasUnread}},
        {provide: ReportProblemPanelService, useValue: {open: openReport}},
        {provide: AlertsStore, useValue: {unreadCount: signal(0)}},
        {
          provide: PaletteEntitiesService,
          useValue: {
            load: () =>
              of({
                holdings: [{symbol: 'AAPL', assetClass: 'equity'}],
                watchlist: [{ticker: 'NVDA'}],
                accounts: [{accountId: 'a1', bankName: 'Monobank', accountNumberLast4: '1234'}],
              }),
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

  it('adds held, watchlist and account entities to the palette', async () => {
    const fixture = await setup(AppRoute.Settings);
    const ids = fixture.componentInstance.paletteItems().map(item => item.id);
    expect(ids).toEqual(
      expect.arrayContaining(['/assets/AAPL', '/assets/NVDA', '/transactions?account=a1'])
    );
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
