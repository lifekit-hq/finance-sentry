import type {Page} from '@playwright/test';

import {API} from './dossier-mocks';

// Ledger proposals (#1069) as `research/recommendations` and the dossier return them. DRAM is the
// first real case: a quarter of the book on a name with no thesis or exit rule behind it.
const BASE = {
  status: 'Pending',
  createdBy: 'Ledger',
  origin: 'Ledger',
  limitCurrency: 'USD',
  venue: 'IBKR',
  decidedAt: null,
  decisionNote: null,
  execution: 'Pending',
  executedAt: null,
  subjectReturnPct: null,
  benchmarkReturnPct: null,
  excessReturnPct: null,
  outcomeScoredAt: null,
};

export const PENDING_DRAM = {
  ...BASE,
  id: 'rec-dram',
  ticker: 'DRAM',
  action: 'Trim',
  triggerKind: 'RiskRule',
  triggerRef: 'single-position-cap',
  rationale:
    'DRAM is 23.1% of the book against a 15% single-name cap, and there is no thesis or exit rule behind it.',
  confidence: 'High',
  invalidationTrigger: 'You write a thesis for DRAM and raise the cap for it.',
  evidence: [
    {kind: 'RiskRule', label: 'Single-name cap 15%, DRAM at 23.1%', refId: null},
    {kind: 'Sleeve', label: 'Unplanned sleeve: not in the IPS', refId: null},
  ],
  sizeUsd: 6420,
  sizeShares: 240,
  shareOfBookBeforePct: 23.1,
  shareOfBookAfterPct: 14.8,
  limitPrice: 26.75,
  orderText: 'SELL 240 DRAM / Limit $26.75 DAY / Account IBKR ••••4821',
  createdAt: '2026-09-10T08:00:00Z',
  expiresAt: '2026-09-24T08:00:00Z',
};

export const PENDING_AAPL = {
  ...BASE,
  id: 'rec-aapl',
  ticker: 'AAPL',
  action: 'Add',
  triggerKind: 'IpsBand',
  triggerRef: 'US Large Cap',
  rationale: 'The US large-cap sleeve sits 6.2 points under its band; AAPL is its largest name.',
  confidence: 'Medium',
  invalidationTrigger: 'The sleeve drifts back inside its band.',
  evidence: [{kind: 'PolicyReview', label: 'Policy review: US Large Cap UnderBand', refId: null}],
  sizeUsd: 1890,
  sizeShares: 10,
  shareOfBookBeforePct: 45.5,
  shareOfBookAfterPct: 48.9,
  limitPrice: 189,
  orderText: 'BUY 10 AAPL / Limit $189.00 DAY / Account IBKR ••••4821',
  createdAt: '2026-09-12T08:00:00Z',
  expiresAt: '2026-09-26T08:00:00Z',
};

export const DECIDED = [
  {
    ...BASE,
    id: 'rec-grab',
    ticker: 'GRAB',
    action: 'Trim',
    status: 'Confirmed',
    triggerKind: 'ThesisBreak',
    triggerRef: 'thesis-grab',
    rationale: 'The take-rate trigger crossed.',
    confidence: 'Medium',
    invalidationTrigger: 'Take rate back above 3%.',
    evidence: [],
    sizeUsd: 2080,
    sizeShares: 660,
    shareOfBookBeforePct: 12.2,
    shareOfBookAfterPct: 9.5,
    limitPrice: 3.13,
    orderText: 'SELL 660 GRAB / Limit $3.13 DAY / Account IBKR ••••4821',
    createdAt: '2026-08-20T08:00:00Z',
    expiresAt: '2026-09-03T08:00:00Z',
    decidedAt: '2026-08-21T09:00:00Z',
    execution: 'Executed',
    executedAt: '2026-08-21T14:30:00Z',
    subjectReturnPct: -4.1,
    benchmarkReturnPct: 1.2,
    excessReturnPct: -5.3,
  },
  {
    ...BASE,
    id: 'rec-nvda',
    ticker: 'NVDA',
    action: 'Exit',
    status: 'Rejected',
    triggerKind: 'RadarSignal',
    triggerRef: 'semis-rotation',
    rationale: 'Money is rotating out of semiconductors.',
    confidence: 'Low',
    invalidationTrigger: 'Semis relative strength turns up.',
    evidence: [],
    sizeUsd: 4300,
    sizeShares: 25,
    shareOfBookBeforePct: 8.4,
    shareOfBookAfterPct: 0,
    limitPrice: 172,
    orderText: 'SELL 25 NVDA / Limit $172.00 DAY / Account IBKR ••••4821',
    createdAt: '2026-08-25T08:00:00Z',
    expiresAt: '2026-09-08T08:00:00Z',
    decidedAt: '2026-08-26T09:00:00Z',
    decisionNote: 'One week of rotation is not a thesis break.',
    execution: 'NotApplicable',
    subjectReturnPct: 6.8,
    benchmarkReturnPct: 1.5,
    excessReturnPct: 5.3,
  },
];

/** The Decisions queue: DRAM first (oldest), then AAPL, with two decided in the last 30 days. */
export async function mockRecommendations(page: Page): Promise<void> {
  await page.route(`${API}/research/recommendations`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({pending: [PENDING_DRAM, PENDING_AAPL], decided: DECIDED}),
    })
  );
}
