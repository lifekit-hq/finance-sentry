import {ChangeDetectionStrategy, Component, signal} from '@angular/core';
import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';

import {ChatWidgetComponent} from '../../modules/agent/components/chat-widget/chat-widget.component';
import {AlertsStore} from '../../modules/alerts/store/alerts/alerts.store';
import {AuthStore} from '../../modules/auth/store/auth.store';
import {AppRoute} from '../../shared/enums/app-route/app-route.enum';
import {AppShellComponent} from './app-shell.component';

@Component({
  selector: 'fns-chat-widget',
  template: '',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
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
            avatarInitials: signal('DT'),
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

  it('shows the store avatar initials in the top bar', async () => {
    const fixture = await setup(AppRoute.Settings);
    expect(fixture.componentInstance.account().label).toBe('DT');
  });
});
