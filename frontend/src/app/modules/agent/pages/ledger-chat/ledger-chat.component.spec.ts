import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type CmnDrawerRef, CmnDrawerService} from '@lifekit-hq/ui';
import {Subject} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {SessionsListComponent} from '../../components/sessions-list/sessions-list.component';
import {AgentChatStore} from '../../store/agent-chat.store';
import {LedgerChatComponent} from './ledger-chat.component';

describe('LedgerChatComponent sessions panel', () => {
  const setup = () => {
    const store = {
      conversations: signal([{id: 'c1', title: 'Budget'}]),
      hasConversations: signal(true),
      activeConversationId: signal(null),
      history: signal([]),
      historyKey: signal(0),
    };
    const closed = new Subject<undefined>();
    const open = vi.fn(
      () => ({afterClosed: () => closed.asObservable()}) as unknown as CmnDrawerRef
    );
    TestBed.configureTestingModule({providers: [{provide: CmnDrawerService, useValue: {open}}]});
    TestBed.overrideComponent(LedgerChatComponent, {
      set: {providers: [{provide: AgentChatStore, useValue: store}]},
    });
    const fixture = TestBed.createComponent(LedgerChatComponent);
    fixture.detectChanges();
    return {fixture, store, open, closed};
  };

  it('pins the conversations list in a labelled aside from md', () => {
    const {fixture} = setup();
    const aside: HTMLElement = fixture.nativeElement.querySelector('aside');
    expect(aside.getAttribute('aria-label')).toBe('Conversations');
    expect(aside.querySelector('fns-sessions-list')).not.toBeNull();
  });

  it('opens the sessions list in the shared drawer, handing it the store', () => {
    const {fixture, store, open} = setup();
    fixture.componentInstance.openSessions();
    expect(open).toHaveBeenCalledWith(
      SessionsListComponent,
      expect.objectContaining({title: 'Sessions', data: store})
    );
  });

  it('returns focus to the control that opened the drawer once it closes', () => {
    const {fixture, closed} = setup();
    const trigger = document.createElement('button');
    document.body.append(trigger);
    trigger.focus();
    fixture.componentInstance.openSessions();
    document.body.focus();
    closed.next(undefined);
    expect(document.activeElement).toBe(trigger);
    trigger.remove();
  });
});
