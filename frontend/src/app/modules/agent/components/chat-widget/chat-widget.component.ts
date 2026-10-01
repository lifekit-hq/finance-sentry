import {ChangeDetectionStrategy, Component, computed, inject, signal} from '@angular/core';
import {toSignal} from '@angular/core/rxjs-interop';
import {NavigationEnd, Router} from '@angular/router';
import {
  ChatComponent,
  type CmnChatStreamFn,
  IconComponent,
  type LucideIconName,
} from '@lifekit-hq/ui';
import {filter, map} from 'rxjs';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {AgentChatStore} from '../../store/agent-chat.store';

@Component({
  selector: 'fns-chat-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [AgentChatStore],
  imports: [ChatComponent, IconComponent],
  templateUrl: './chat-widget.component.html',
})
export class ChatWidgetComponent {
  private readonly router = inject(Router);
  private readonly routerUrl = toSignal(
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      map(e => e.urlAfterRedirects)
    ),
    {initialValue: this.router.url}
  );

  public readonly store = inject(AgentChatStore);
  public readonly isOpen = signal(false);
  // The full-page Ledger already hosts a chat — the floating widget would be redundant there.
  public readonly isHidden = computed(() => this.routerUrl().startsWith(AppRoute.Ledger));

  protected readonly openIcon: LucideIconName = 'Sparkles';
  protected readonly closeIcon: LucideIconName = 'X';
  protected readonly newChatIcon: LucideIconName = 'Plus';

  public readonly chatStream: CmnChatStreamFn = text => this.store.stream(text);

  public toggle(): void {
    this.isOpen.update(open => !open);
  }

  public onNewChat(): void {
    this.store.resetThread();
  }
}
