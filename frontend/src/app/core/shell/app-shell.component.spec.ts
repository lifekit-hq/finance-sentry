import {BreakpointObserver} from '@angular/cdk/layout';
import {ChangeDetectionStrategy, Component, signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {of} from 'rxjs';

import {ChatWidgetComponent} from '../../modules/agent/components/chat-widget/chat-widget.component';
import {AlertsStore} from '../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {AppShellComponent} from './app-shell.component';
import {PaletteEntitiesService} from './services/palette-entities.service';

@Component({
  selector: 'fns-chat-widget',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class ChatWidgetStubComponent {}

/** `CmnShellService` derives phone / rail / sidebar from these width queries. */
const wideBreakpoints = (wide: boolean): Record<string, boolean> => ({
  '(min-width: 600px)': wide,
  '(min-width: 840px)': wide,
});

describe('AppShellComponent FAB clearance', () => {
  const canUseAi = signal(true);

  const setup = async (url: string, wide = false): Promise<ComponentFixture<AppShellComponent>> => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: BreakpointObserver,
          useValue: {
            observe: () => of({matches: wide, breakpoints: wideBreakpoints(wide)}),
            isMatched: () => wide,
          },
        },
        provideRouter([{path: '**', children: []}]),
        {
          provide: AuthStore,
          useValue: {
            canUseAi,
            permissions: signal([]),
            avatarInitials: signal('DT'),
            logout: () => undefined,
          },
        },
        {provide: AlertsStore, useValue: {unreadCount: signal(0)}},
        {
          provide: PaletteEntitiesService,
          useValue: {
            load: () =>
              of({
                holdings: [{symbol: 'AAPL', isVenueCash: false}],
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
});
