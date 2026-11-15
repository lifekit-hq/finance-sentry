import {type ThesisDto, type ThesisInvalidationTrigger} from '../thesis/thesis.model';
import {type ThesisRevisionContent, type ThesisRevisionDto} from './thesis-revision.model';

/** Spec fixtures for the thesis-revision confirm surface; never imported by app code. */
export class ThesisRevisionFixtures {
  public static trigger(
    overrides: Partial<ThesisInvalidationTrigger> = {}
  ): ThesisInvalidationTrigger {
    return {
      metric: 'gross_margin',
      direction: 'lessThan',
      threshold: 0.35,
      proxyTicker: null,
      consecutivePeriods: 2,
      periodType: 'Quarter',
      ...overrides,
    };
  }

  public static content(overrides: Partial<ThesisRevisionContent> = {}): ThesisRevisionContent {
    return {
      thesisText: null,
      keyDataPoints: null,
      catalysts: null,
      invalidationTriggers: null,
      narrativeFalsifiers: null,
      entryPrice: null,
      ...overrides,
    };
  }

  /** A held thesis on CBRS, the subject of a narrative update or trigger change. */
  public static thesis(overrides: Partial<ThesisDto> = {}): ThesisDto {
    return {
      id: 'thesis-1',
      ticker: 'CBRS',
      thesisText: 'Wafer-scale inference wins on cost per token.',
      keyDataPoints: [],
      catalysts: [],
      invalidationTriggers: [ThesisRevisionFixtures.trigger()],
      createdAt: '2026-10-01T09:00:00Z',
      updatedAt: '2026-10-01T09:00:00Z',
      brokenAt: null,
      brokenReason: null,
      entryPrice: null,
      origin: 'User',
      narrativeFalsifiers: [],
      ...overrides,
    };
  }

  /** A pending narrative update on CBRS that rests on one stored article and one filing. */
  public static revision(overrides: Partial<ThesisRevisionDto> = {}): ThesisRevisionDto {
    return {
      id: 'rev-1',
      kind: 'NarrativeUpdate',
      status: 'Pending',
      proposedBy: 'Ledger',
      subjectType: 'Thesis',
      subjectId: 'thesis-1',
      ticker: 'CBRS',
      proposed: ThesisRevisionFixtures.content({
        thesisText:
          'Wafer-scale inference wins on cost per token; the S-1 confirms two hyperscaler anchors.',
      }),
      rationale: 'The S-1 names two anchor customers, which the original case only assumed.',
      evidence: {
        newsArticleIds: ['article-1'],
        filingAccessions: ['0001-26-1'],
        eventIds: null,
        fundamentalsAsOf: null,
      },
      evidenceLinks: [
        {kind: 'News', label: 'Cerebras files for IPO', url: 'https://example.com/cbrs'},
        {kind: 'Filing', label: 'Filing 0001-26-1', url: null},
      ],
      proposedUsd: 0,
      paper: false,
      createdAt: '2026-11-10T09:00:00Z',
      expiresAt: '2026-11-24T09:00:00Z',
      decidedAt: null,
      decisionNote: null,
      resultThesisId: null,
      ...overrides,
    };
  }
}
