/** How far to trust a figure; the backend's `HeadlineQuality`, worst of its reasons. */
export type HeadlineQuality = 'COMPLETE' | 'ESTIMATED' | 'DEGRADED' | 'UNAVAILABLE';

/** Whether the cost basis or start value behind a percent covers the whole scope. */
export type HeadlineBasisStatus = 'COMPLETE' | 'PARTIAL' | 'MISSING';

/**
 * One typed reason a figure is less than complete. `code` is a string, not the known-code union:
 * a code the backend adds later must reach the sheet and fall back to a generic line, not break it.
 */
export interface HeadlineReason {
  code: string;
  /** The source a per-source reason is about (a stale sleeve or provider). */
  source?: Nullable<string>;
}

export interface HeadlineWindow {
  /** ISO date; absent for a point-in-time value. */
  from?: Nullable<string>;
  to: string;
}

/**
 * The typed record every top-line figure carries (finance-framework §3.6). Always read beside the
 * response's existing fields, and absent from older API builds, so every reader treats a missing
 * one as "no quality information", never as a complete figure.
 */
export interface Headline {
  /** In `currency`; null when the figure is unavailable, never 0 in its place. */
  amount: Nullable<number>;
  /** Percentage points; null unless the method and coverage allow one. */
  percent: Nullable<number>;
  method: string;
  basis?: Nullable<string>;
  scope: string;
  window: HeadlineWindow;
  quality: HeadlineQuality;
  basisStatus?: Nullable<HeadlineBasisStatus>;
  /** Share (0..1) of the scope's value the figure measures; null when it measures all of it. */
  coverage?: Nullable<number>;
  reasons: HeadlineReason[];
  /** The oldest input timestamp behind the figure (ISO). */
  asOf?: Nullable<string>;
  currency: string;
}

/** The badge a non-complete figure wears. */
export type HeadlineBadgeKind = 'estimated' | 'stale' | 'partial' | 'unavailable';

export interface HeadlineBadge {
  kind: HeadlineBadgeKind;
  label: string;
}

/** What the reason sheet opens with. */
export interface HeadlineReasonSheetData {
  headline: Headline;
  badge: HeadlineBadge;
}

/** A headline as read off the wire: absent from an older API build, null when withheld. */
export type MaybeHeadline = Headline | null | undefined;
