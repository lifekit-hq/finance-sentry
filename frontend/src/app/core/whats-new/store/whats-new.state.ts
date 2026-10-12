import {type WhatsNewVersion} from '../models/whats-new.model';

export interface WhatsNewState {
  /** The app version the panel was last opened for on this device; null until the store has read it. */
  lastSeen: Nullable<string>;
  versions: WhatsNewVersion[];
  /** `whats-new.json` has been read; separates "not asked yet" from "nothing in it". */
  loaded: boolean;
  status: AsyncStatus;
}

export const initialWhatsNewState: WhatsNewState = {
  lastSeen: null,
  versions: [],
  loaded: false,
  status: 'idle',
};
