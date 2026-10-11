import {type CmnChatMessage} from '@lifekit-hq/ui';

import {type ConversationSummary} from '../models/conversation/conversation.model';

export interface AgentChatState {
  conversations: ConversationSummary[];
  activeConversationId: string | null;
  // Preloaded history for the active thread — fed to <cmn-chat> when it (re)mounts.
  history: CmnChatMessage[];
  // Bumped on new-chat / conversation-switch to remount <cmn-chat>; NOT on first-turn id capture.
  threadNonce: number;
}

export const initialAgentChatState: AgentChatState = {
  conversations: [],
  activeConversationId: null,
  history: [],
  threadNonce: 0,
};
