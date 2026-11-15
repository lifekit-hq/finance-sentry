import type {Page} from '@playwright/test';

import {API} from './dossier-mocks';

// Thesis changes Ledger proposes (spec 050) as `research/thesis-revisions` returns them. The owner is the only
// one who confirms; the queue read also carries the evidence links the server resolved.
const BASE = {
  status: 'Pending',
  proposedBy: 'Ledger',
  subjectType: 'Thesis',
  proposedUsd: 0,
  paper: false,
  decidedAt: null,
  decisionNote: null,
  resultThesisId: null,
};

const EMPTY_CONTENT = {
  thesisText: null,
  keyDataPoints: null,
  catalysts: null,
  invalidationTriggers: null,
  narrativeFalsifiers: null,
  entryPrice: null,
};

/** A narrative update on the AAPL thesis the dossier mock carries, so the card has a real "before". */
export const REVISION_AAPL = {
  ...BASE,
  id: 'rev-aapl',
  kind: 'NarrativeUpdate',
  subjectId: 'thesis-1',
  ticker: 'AAPL',
  proposed: {
    ...EMPTY_CONTENT,
    thesisText:
      'Apple continues to expand its services revenue and ecosystem lock-in; the 10-K shows services at 28% of revenue.',
  },
  rationale: 'The 10-K puts services at 28% of revenue, which the original case only assumed.',
  evidence: {
    newsArticleIds: ['article-1'],
    filingAccessions: ['0000320193-26-000123'],
    eventIds: null,
    fundamentalsAsOf: null,
  },
  evidenceLinks: [
    {kind: 'News', label: 'Apple services revenue hits a record', url: 'https://example.com/aapl'},
    {kind: 'Filing', label: 'Filing 0000320193-26-000123', url: null},
  ],
  createdAt: '2026-09-10T08:00:00Z',
  expiresAt: '2026-09-24T08:00:00Z',
};

/** A trigger change on a held thesis: one trigger added, the existing one kept. */
export const REVISION_CBRS = {
  ...BASE,
  id: 'rev-cbrs',
  kind: 'TriggerChange',
  subjectId: 'thesis-cbrs',
  ticker: 'CBRS',
  proposed: {
    ...EMPTY_CONTENT,
    invalidationTriggers: [
      {
        metric: 'gross_margin',
        direction: 'lessThan',
        threshold: 0.35,
        proxyTicker: null,
        consecutivePeriods: 2,
        periodType: 'Quarter',
      },
      {
        metric: 'price_drawdown',
        direction: 'greaterThan',
        threshold: 0.4,
        proxyTicker: null,
        consecutivePeriods: 1,
        periodType: 'Quarter',
      },
    ],
  },
  rationale: 'The thesis has no price-based exit; a 40% drawdown should break it.',
  evidence: null,
  evidenceLinks: null,
  createdAt: '2026-09-11T08:00:00Z',
  expiresAt: '2026-09-25T08:00:00Z',
};

export const REVISION_DECIDED = {
  ...BASE,
  id: 'rev-nvda',
  kind: 'Close',
  status: 'Rejected',
  subjectId: 'thesis-nvda',
  ticker: 'NVDA',
  proposed: EMPTY_CONTENT,
  rationale: 'Rotation out of semis.',
  evidence: null,
  evidenceLinks: null,
  createdAt: '2026-08-25T08:00:00Z',
  expiresAt: '2026-09-08T08:00:00Z',
  decidedAt: '2026-08-26T09:00:00Z',
  decisionNote: 'One week of rotation is not a thesis break.',
};

export const THESES = [
  {
    id: 'thesis-cbrs',
    ticker: 'CBRS',
    thesisText: 'Wafer-scale inference wins on cost per token.',
    keyDataPoints: [],
    catalysts: [],
    invalidationTriggers: [
      {
        metric: 'gross_margin',
        direction: 'lessThan',
        threshold: 0.35,
        proxyTicker: null,
        consecutivePeriods: 2,
        periodType: 'Quarter',
      },
    ],
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    brokenAt: null,
    brokenReason: null,
    entryPrice: null,
    origin: 'User',
    narrativeFalsifiers: [],
  },
  {
    id: 'thesis-1',
    ticker: 'AAPL',
    thesisText: 'Apple continues to expand its services revenue and ecosystem lock-in.',
    keyDataPoints: [],
    catalysts: [],
    invalidationTriggers: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    brokenAt: null,
    brokenReason: null,
    entryPrice: 175,
    origin: 'User',
    narrativeFalsifiers: [],
  },
];

interface Queue {
  pending?: unknown[];
  decided?: unknown[];
}

/** The owner's thesis-change queue (empty unless given) and the theses its cards read as their "before". */
export async function mockThesisRevisions(page: Page, queue: Queue = {}): Promise<void> {
  await page.route(`${API}/research/thesis-revisions`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({pending: queue.pending ?? [], decided: queue.decided ?? []}),
    })
  );
  await page.route(`${API}/research/theses`, route =>
    route.fulfill({status: 200, contentType: 'application/json', body: JSON.stringify(THESES)})
  );
}
