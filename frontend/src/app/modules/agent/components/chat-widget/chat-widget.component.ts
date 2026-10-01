import {ChangeDetectionStrategy, Component, inject, signal} from '@angular/core';
import {
  ChatComponent,
  type CmnChatStreamFn,
  IconComponent,
  type LucideIconName,
} from '@lifekit-hq/ui';

import {AgentChatStore} from '../../store/agent-chat.store';

@Component({
  selector: 'fns-chat-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [AgentChatStore],
  imports: [ChatComponent, IconComponent],
  templateUrl: './chat-widget.component.html',
})
export class ChatWidgetComponent {
  public readonly store = inject(AgentChatStore);
  public readonly isOpen = signal(false);

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
