import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {CMN_DRAWER_DATA, CmnDrawerRef} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {SessionsListComponent} from './sessions-list.component';

describe('SessionsListComponent', () => {
  const makeStore = () => ({
    conversations: signal([{id: 'c1', title: 'Budget'}]),
    hasConversations: signal(true),
    activeConversationId: signal(null),
    resetThread: vi.fn(),
    selectConversation: vi.fn(),
    deleteConversation: vi.fn(),
  });

  it('acts on the store input and leaves no drawer to close', () => {
    const store = makeStore();
    const fixture = TestBed.createComponent(SessionsListComponent);
    fixture.componentRef.setInput('pageStore', store as never);
    fixture.detectChanges();
    fixture.componentInstance.onSelect({id: 'c1'} as never);
    fixture.componentInstance.onConversationAction('delete', {id: 'c1'} as never);
    expect(store.selectConversation).toHaveBeenCalledWith('c1');
    expect(store.deleteConversation).toHaveBeenCalledWith('c1');
  });

  it('in the drawer reads the store from drawer data, focuses in, and closes after a pick', async () => {
    const store = makeStore();
    const close = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        {provide: CMN_DRAWER_DATA, useValue: store},
        {provide: CmnDrawerRef, useValue: {close}},
      ],
    });
    const fixture = TestBed.createComponent(SessionsListComponent);
    document.body.append(fixture.nativeElement);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.contains(document.activeElement)).toBe(true);
    fixture.componentInstance.onNewChat();
    fixture.componentInstance.onSelect({id: 'c1'} as never);
    expect(store.resetThread).toHaveBeenCalled();
    expect(close).toHaveBeenCalledTimes(2);
    fixture.nativeElement.remove();
  });

  it('keeps the actions menu a labelled trigger, not a destructive button', () => {
    const fixture = TestBed.createComponent(SessionsListComponent);
    fixture.componentRef.setInput('pageStore', makeStore() as never);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('cmn-menu')).not.toBeNull();
    expect(el.querySelector('cmn-button[variant="destructive"]')).toBeNull();
  });
});
