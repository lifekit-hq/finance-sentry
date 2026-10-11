import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {agentChatMethods} from './agent-chat.methods';
import {initialAgentChatState} from './agent-chat.state';

function build() {
  const store = signalState(initialAgentChatState);
  return {store, methods: agentChatMethods(store)};
}

describe('agentChatMethods', () => {
  it('sets the active conversation id without remounting (no nonce bump)', () => {
    const {store, methods} = build();
    methods.setActiveConversationId('c1');

    expect(store.activeConversationId()).toBe('c1');
    expect(store.threadNonce()).toBe(0);
  });

  it('resetThread clears the thread and bumps the nonce to force a remount', () => {
    const {store, methods} = build();
    methods.setActiveConversationId('c1');
    methods.resetThread();

    expect(store.activeConversationId()).toBeNull();
    expect(store.history()).toEqual([]);
    expect(store.threadNonce()).toBe(1);
  });

  it('openConversation loads history, sets the id, and bumps the nonce', () => {
    const {store, methods} = build();
    methods.openConversation('c1', [{role: 'user', text: 'hi'}]);

    expect(store.activeConversationId()).toBe('c1');
    expect(store.history()).toEqual([{role: 'user', text: 'hi'}]);
    expect(store.threadNonce()).toBe(1);
  });

  it('removes a conversation and clears the thread when it was active', () => {
    const {store, methods} = build();
    methods.setConversations([
      {id: 'c1', title: 'a', updatedAt: '', modelId: 'm'},
      {id: 'c2', title: 'b', updatedAt: '', modelId: 'm'},
    ]);
    methods.openConversation('c1', [{role: 'ai', text: 'hello'}]);

    methods.removeConversationLocally('c1');

    expect(store.conversations().map(c => c.id)).toEqual(['c2']);
    expect(store.activeConversationId()).toBeNull();
    expect(store.history()).toEqual([]);
  });

  it('removes a non-active conversation without touching the open thread', () => {
    const {store, methods} = build();
    methods.setConversations([
      {id: 'c1', title: 'a', updatedAt: '', modelId: 'm'},
      {id: 'c2', title: 'b', updatedAt: '', modelId: 'm'},
    ]);
    methods.openConversation('c1', [{role: 'ai', text: 'hello'}]);

    methods.removeConversationLocally('c2');

    expect(store.conversations().map(c => c.id)).toEqual(['c1']);
    expect(store.activeConversationId()).toBe('c1');
    expect(store.history()).toEqual([{role: 'ai', text: 'hello'}]);
  });
});
