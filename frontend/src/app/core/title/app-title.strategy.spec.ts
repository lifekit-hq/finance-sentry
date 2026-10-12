import {ChangeDetectionStrategy, Component, signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {Title} from '@angular/platform-browser';
import {provideRouter, Router} from '@angular/router';
import {CmnShellService} from '@lifekit-hq/ui';
import {of} from 'rxjs';

import {ChatWidgetComponent} from '../../modules/agent/components/chat-widget/chat-widget.component';
import {AlertsStore} from '../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {provideTitleStrategy} from '../providers/title-strategy.provider';
import {AppShellComponent} from '../shell/app-shell.component';
import {PaletteEntitiesService} from '../shell/services/palette-entities.service';
import {WhatsNewPanelService} from '../whats-new/services/whats-new-panel.service';
import {RouteTitleUtils} from './route-title.utils';

@Component({template: '', changeDetection: ChangeDetectionStrategy.OnPush})
class PageStubComponent {}

@Component({
  selector: 'fns-chat-widget',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class ChatWidgetStubComponent {}

describe('AppTitleStrategy', () => {
  const setup = (): Router => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: '',
            component: AppShellComponent,
            children: [
              {path: 'budgets', component: PageStubComponent, ...RouteTitleUtils.of('Budgets')},
              {
                path: 'assets/:symbol',
                component: PageStubComponent,
                ...RouteTitleUtils.of(route => route.paramMap.get('symbol')?.toUpperCase() ?? ''),
              },
              {path: 'untitled', component: PageStubComponent},
            ],
          },
        ]),
        provideTitleStrategy(),
        {provide: CmnShellService, useValue: {mode: signal('phone'), isPhone: signal(true)}},
        {
          provide: AuthStore,
          useValue: {
            canUseAi: signal(false),
            permissions: signal([]),
            avatarInitials: signal('DT'),
            logout: () => undefined,
          },
        },
        {provide: WhatsNewPanelService, useValue: {open: vi.fn()}},
        {provide: AlertsStore, useValue: {unreadCount: signal(0)}},
        {
          provide: PaletteEntitiesService,
          useValue: {load: () => of({holdings: [], watchlist: [], accounts: []})},
        },
      ],
    });
    TestBed.overrideComponent(AppShellComponent, {
      remove: {imports: [ChatWidgetComponent]},
      add: {imports: [ChatWidgetStubComponent]},
    });
    return TestBed.inject(Router);
  };

  it('sets the tab title from the active route', async () => {
    const router = setup();
    await router.navigateByUrl('/budgets');
    expect(TestBed.inject(Title).getTitle()).toBe('Budgets · Finance Sentry');
  });

  it('uses a title resolved from the route', async () => {
    const router = setup();
    await router.navigateByUrl('/assets/aapl');
    expect(TestBed.inject(Title).getTitle()).toBe('AAPL · Finance Sentry');
  });

  it('falls back to the app name when the route has no title', async () => {
    const router = setup();
    await router.navigateByUrl('/untitled');
    expect(TestBed.inject(Title).getTitle()).toBe('Finance Sentry');
  });

  it('shows the same title in the page header', async () => {
    const router = setup();
    await router.navigateByUrl('/budgets');
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    const header = (fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent?.trim();
    expect(TestBed.inject(Title).getTitle()).toBe(`${header} · Finance Sentry`);
  });
});
