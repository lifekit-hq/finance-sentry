import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  ButtonComponent,
  ChatComponent,
  type CmnChatStreamFn,
  IconComponent,
  type LucideIconName,
  MenuComponent,
  type MenuItem,
} from '@lifekit-hq/ui';

import {type ConversationSummary} from '../../models/conversation/conversation.model';
import {AgentChatStore} from '../../store/agent-chat.store';

const CONVERSATION_MENU_ITEMS: MenuItem[] = [
  {id: 'delete', label: 'Delete', icon: 'Trash2', destructive: true},
];

@Component({
  selector: 'fns-ledger-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block h-full'},
  providers: [AgentChatStore],
  imports: [ButtonComponent, ChatComponent, IconComponent, MenuComponent],
  templateUrl: './ledger-chat.component.html',
})
export class LedgerChatComponent {
  public readonly store = inject(AgentChatStore);

  protected readonly newChatIcon: LucideIconName = 'Plus';
  protected readonly sessionsIcon: LucideIconName = 'History';
  protected readonly conversationMenuItems = CONVERSATION_MENU_ITEMS;

  public readonly chatStream: CmnChatStreamFn = text => this.store.stream(text);

  public onNewChat(): void {
    this.store.resetThread();
  }

  public onSelect(conversation: ConversationSummary): void {
    this.store.selectConversation(conversation.id);
  }

  public onConversationAction(actionId: string, conversation: ConversationSummary): void {
    if (actionId === 'delete') {
      this.store.deleteConversation(conversation.id);
    }
  }
}
