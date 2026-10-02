import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {describe, expect, it, vi} from 'vitest';

import {AgentChatStore} from '../../store/agent-chat.store';
import {LedgerChatComponent} from './ledger-chat.component';

describe('LedgerChatComponent conversation actions', () => {
  const setup = () => {
    const store = {
      conversations: signal([{id: 'c1', title: 'Budget'}]),
      hasConversations: signal(true),
      activeConversationId: signal(null),
      sessionsOpen: signal(false),
      history: signal([]),
      historyKey: signal(0),
      setSessionsOpen: vi.fn(),
      deleteConversation: vi.fn(),
    };
    TestBed.overrideComponent(LedgerChatComponent, {
      set: {providers: [{provide: AgentChatStore, useValue: store}]},
    });
    const fixture = TestBed.createComponent(LedgerChatComponent);
    fixture.detectChanges();
    return {fixture, store};
  };

  it('renders a labelled actions menu trigger instead of a destructive button', () => {
    const {fixture} = setup();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('cmn-menu')).not.toBeNull();
    expect(el.querySelector('cmn-button[variant="destructive"]')).toBeNull();
  });

  it('deletes the conversation when the delete action is selected', () => {
    const {fixture, store} = setup();
    fixture.componentInstance.onConversationAction('delete', {id: 'c1'} as never);
    expect(store.deleteConversation).toHaveBeenCalledWith('c1');
  });
});
