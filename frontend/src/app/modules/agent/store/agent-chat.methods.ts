import {type CmnChatMessage} from '@lifekit-hq/ui';
import {getState, patchState, type WritableStateSource} from '@ngrx/signals';

import {type ConversationSummary} from '../models/conversation/conversation.model';
import {type AgentChatState} from './agent-chat.state';

export function agentChatMethods(store: WritableStateSource<AgentChatState>) {
  return {
    setConversations(conversations: ConversationSummary[]): void {
      patchState(store, {conversations});
    },

    // Captured mid-turn when the server assigns a new thread — must NOT remount (no nonce bump).
    setActiveConversationId(activeConversationId: string | null): void {
      patchState(store, {activeConversationId});
    },

    resetThread(): void {
      patchState(store, state => ({
        activeConversationId: null,
        history: [],
        threadNonce: state.threadNonce + 1,
      }));
    },

    openConversation(id: string, history: CmnChatMessage[]): void {
      patchState(store, state => ({
        activeConversationId: id,
        history,
        threadNonce: state.threadNonce + 1,
      }));
    },

    removeConversationLocally(id: string): void {
      const state = getState(store);
      const wasActive = state.activeConversationId === id;
      patchState(store, {
        conversations: state.conversations.filter(c => c.id !== id),
        ...(wasActive
          ? {activeConversationId: null, history: [], threadNonce: state.threadNonce + 1}
          : {}),
      });
    },
  };
}
