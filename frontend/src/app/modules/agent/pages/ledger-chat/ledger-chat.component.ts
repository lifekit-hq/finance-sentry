import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  ButtonComponent,
  ChatComponent,
  type CmnChatStreamFn,
  type LucideIconName,
} from '@lifekit-hq/ui';

import {type ConversationSummary} from '../../models/conversation/conversation.model';
import {AgentChatStore} from '../../store/agent-chat.store';

@Component({
  selector: 'fns-ledger-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block h-full'},
  providers: [AgentChatStore],
  imports: [ButtonComponent, ChatComponent],
  templateUrl: './ledger-chat.component.html',
})
export class LedgerChatComponent {
  public readonly store = inject(AgentChatStore);

  protected readonly newChatIcon: LucideIconName = 'Plus';
  protected readonly sessionsIcon: LucideIconName = 'History';
  protected readonly deleteIcon: LucideIconName = 'Trash2';

  public readonly chatStream: CmnChatStreamFn = text => this.store.stream(text);

  public onNewChat(): void {
    this.store.resetThread();
  }

  public onSelect(conversation: ConversationSummary): void {
    this.store.selectConversation(conversation.id);
  }

  public onDelete(conversation: ConversationSummary, event: Event): void {
    event.stopPropagation();
    this.store.deleteConversation(conversation.id);
  }
}
