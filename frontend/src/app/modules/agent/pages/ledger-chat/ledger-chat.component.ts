import {DOCUMENT} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  ButtonComponent,
  ChatComponent,
  type CmnChatStreamFn,
  CmnDrawerService,
  type LucideIconName,
} from '@lifekit-hq/ui';

import {SessionsListComponent} from '../../components/sessions-list/sessions-list.component';
import {AgentChatStore} from '../../store/agent-chat.store';

const SESSIONS_TITLE = 'Sessions';

@Component({
  selector: 'fns-ledger-chat',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block h-full'},
  providers: [AgentChatStore],
  imports: [ButtonComponent, ChatComponent, SessionsListComponent],
  templateUrl: './ledger-chat.component.html',
})
export class LedgerChatComponent {
  private readonly drawer = inject(CmnDrawerService);
  private readonly document = inject(DOCUMENT);

  public readonly store = inject(AgentChatStore);

  protected readonly sessionsIcon: LucideIconName = 'History';

  public readonly chatStream: CmnChatStreamFn = text => this.store.stream(text);

  /** Phone only: the drawer carries the store because it is created outside this page's injector. */
  public openSessions(): void {
    const trigger = this.document.activeElement;
    const ref = this.drawer.open(SessionsListComponent, {title: SESSIONS_TITLE, data: this.store});
    ref.afterClosed().subscribe(() => {
      if (trigger instanceof HTMLElement) {
        trigger.focus();
      }
    });
  }
}
