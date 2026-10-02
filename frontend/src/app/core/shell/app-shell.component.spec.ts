import {ChangeDetectionStrategy, Component, signal} from '@angular/core';
import {ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {AppLayoutComponent} from '@lifekit-hq/ui';

import {ChatWidgetComponent} from '../../modules/agent/components/chat-widget/chat-widget.component';
import {AlertsStore} from '../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {AppShellComponent} from './app-shell.component';
import {FAB_CLEARANCE} from './app-shell.constants';

@Component({selector: 'fns-chat-widget', template: '', changeDetection: ChangeDetectionStrategy.OnPush})
class ChatWidgetStubComponent {}

describe('AppShellComponent FAB clearance', () => {
  const canUseAi = signal(true);

  const setup = async (url: string): Promise<ComponentFixture<AppShellComponent>> => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{path: '**', children: []}]),
        {
          provide: AuthStore,
          useValue: {
            canUseAi,
            permissions: signal([]),
            email: signal('a@b.c'),
            logout: () => undefined,
          },
        },
        {provide: AlertsStore, useValue: {unreadCount: signal(0)}},
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

  const wrapper = (fixture: ComponentFixture<AppShellComponent>): HTMLElement =>
    fixture.nativeElement.querySelector('cmn-app-layout .overflow-y-auto.h-full');

  beforeEach(() => canUseAi.set(true));

  it('pads the scroll wrapper past the FAB for an AI user on a page', async () => {
    const fixture = await setup(AppRoute.Settings);
    expect(wrapper(fixture).style.paddingBottom).toBe(FAB_CLEARANCE);
  });

  it('adds no padding on the full-page Ledger', async () => {
    const fixture = await setup(AppRoute.Ledger);
    expect(wrapper(fixture).style.paddingBottom).toBe('');
  });

  it('adds no padding for a user without ai.use', async () => {
    canUseAi.set(false);
    const fixture = await setup(AppRoute.Settings);
    expect(wrapper(fixture).style.paddingBottom).toBe('');
  });
});
