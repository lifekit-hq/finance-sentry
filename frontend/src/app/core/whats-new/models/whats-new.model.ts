/** One plain-language line of a release; an owner line is hidden from Members. */
export interface WhatsNewNote {
  text: string;
  owner: boolean;
}

export interface WhatsNewVersion {
  version: string;
  /** ISO date (`2026-10-05`) from the changelog heading; null when the heading carries none. */
  date: Nullable<string>;
  notes: WhatsNewNote[];
  /** The filtered changelog entries (features and fixes), as sentences. */
  changes: string[];
}

/** The shape of `whats-new.json`, written by `scripts/build-whats-new.mjs`. */
export interface WhatsNewData {
  versions: WhatsNewVersion[];
}
