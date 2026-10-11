import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  Injector,
  input,
} from '@angular/core';
import {
  ButtonComponent,
  CMN_DRAWER_DATA,
  CmnDrawerRef,
  IconComponent,
  type LucideIconName,
  MenuComponent,
  type MenuItem,
} from '@lifekit-hq/ui';

import {type ConversationSummary} from '../../models/conversation/conversation.model';
import {AgentChatStore} from '../../store/agent-chat.store';

type ChatStore = InstanceType<typeof AgentChatStore>;

const CONVERSATION_MENU_ITEMS: MenuItem[] = [
  {id: 'delete', label: 'Delete', icon: 'Trash2', destructive: true},
];

/**
 * The conversation list: pinned beside the chat from md, opened in a `CmnDrawerService` bottom sheet
 * below it. The drawer cannot see the page-scoped store, so the page hands it over as drawer data.
 */
@Component({
  selector: 'fns-sessions-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'flex h-full min-h-0 flex-col gap-cmn-3', '[class.p-cmn-4]': 'isInDrawer'},
  imports: [ButtonComponent, IconComponent, MenuComponent],
  templateUrl: './sessions-list.component.html',
})
export class SessionsListComponent {
  private readonly drawerRef = inject(CmnDrawerRef, {optional: true});
  private readonly drawerStore = inject<ChatStore | null>(CMN_DRAWER_DATA, {optional: true});
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  public readonly pageStore = input<ChatStore | null>(null);

  protected readonly isInDrawer = this.drawerRef !== null;
  protected readonly newChatIcon: LucideIconName = 'Plus';
  protected readonly conversationMenuItems = CONVERSATION_MENU_ITEMS;

  constructor() {
    if (this.isInDrawer) {
      // The drawer opens without moving focus; put it on the first control so the keyboard lands inside.
      afterNextRender(() => this.host.nativeElement.querySelector('button')?.focus(), {
        injector: inject(Injector),
      });
    }
  }

  protected get store(): ChatStore {
    const store = this.pageStore() ?? this.drawerStore;
    if (!store) {
      throw new Error('SessionsListComponent needs a store input or drawer data.');
    }
    return store;
  }

  public onNewChat(): void {
    this.store.resetThread();
    this.drawerRef?.close();
  }

  public onSelect(conversation: ConversationSummary): void {
    this.store.selectConversation(conversation.id);
    this.drawerRef?.close();
  }

  public onConversationAction(actionId: string, conversation: ConversationSummary): void {
    if (actionId === 'delete') {
      this.store.deleteConversation(conversation.id);
    }
  }
}
