import {describe, expect, it} from 'vitest';

import {ScorecardFixtures} from '../../../shared/models/scorecard/scorecard.fixtures';
import {type ThesisDto} from '../models/dossier/dossier.model';
import {DossierVerdictUtils} from './dossier-verdict.utils';

function thesis(overrides: Partial<ThesisDto> = {}): ThesisDto {
  return {
    id: 't1',
    ticker: 'ABCD',
    thesisText: 'A plan.',
    keyDataPoints: [],
    catalysts: [],
    invalidationTriggers: [],
    createdAt: '2026-10-01T00:00:00Z',
    updatedAt: '2026-10-01T00:00:00Z',
    brokenAt: null,
    brokenReason: null,
    entryPrice: null,
    ...overrides,
  };
}

const KILL_RULE = {
  metric: 'gross_margin',
  direction: 'lessThan',
  threshold: 0.35,
  proxyTicker: null,
  consecutivePeriods: 2,
  periodType: 'quarter',
};

describe('DossierVerdictUtils.thesisStatus', () => {
  it('reports a missing thesis with no kill-switch', () => {
    expect(DossierVerdictUtils.thesisStatus(null)).toEqual({
      state: 'missing',
      thesisLabel: 'No thesis on file',
      killSwitchLabel: null,
      rules: [],
      brokenReason: null,
    });
  });

  it('says plainly when a thesis has no kill rules', () => {
    const status = DossierVerdictUtils.thesisStatus(thesis());

    expect(status.state).toBe('active');
    expect(status.killSwitchLabel).toBe('No kill-switch rules');
  });

  it('counts the armed kill rules, singular and plural, and words each one', () => {
    const one = DossierVerdictUtils.thesisStatus(thesis({invalidationTriggers: [KILL_RULE]}));
    const two = DossierVerdictUtils.thesisStatus(
      thesis({invalidationTriggers: [KILL_RULE, {...KILL_RULE, metric: 'revenue_yoy'}]})
    );

    expect(one.killSwitchLabel).toBe('Kill-switch armed: 1 rule');
    expect(one.rules).toEqual(['Gross margin falls below 35.0%']);
    expect(two.killSwitchLabel).toBe('Kill-switch armed: 2 rules');
  });

  it('reports a tripped kill-switch with the reason', () => {
    const status = DossierVerdictUtils.thesisStatus(
      thesis({
        invalidationTriggers: [KILL_RULE],
        brokenAt: '2026-10-20T00:00:00Z',
        brokenReason: 'Gross margin fell below 35.0% for 2 quarters.',
      })
    );

    expect(status.state).toBe('tripped');
    expect(status.thesisLabel).toBe('Thesis invalidated');
    expect(status.killSwitchLabel).toBe('Kill-switch tripped');
    expect(status.brokenReason).toBe('Gross margin fell below 35.0% for 2 quarters.');
  });
});

describe('DossierVerdictUtils.header', () => {
  const scorecard = ScorecardFixtures.scorecard();

  it('has no header without a scorecard', () => {
    expect(DossierVerdictUtils.header(null, true, thesis())).toBeNull();
  });

  it('words the verdict in sentence case and tints it by the verdict block tones', () => {
    expect(DossierVerdictUtils.header(scorecard, false, null)?.status).toEqual({
      tone: 'error',
      label: 'Avoid',
    });
  });

  it.each([
    ['StrongBuy', 'success', 'Strong buy'],
    ['Buy', 'success', 'Buy'],
    ['Wait', 'neutral', 'Wait'],
    ['NotScored', 'neutral', 'Not scored'],
  ] as const)('maps %s to a %s %s', (word, tone, label) => {
    const scored = ScorecardFixtures.scorecard({
      verdict: {...scorecard.verdict, word},
    });

    expect(DossierVerdictUtils.header(scored, false, null)?.status).toEqual({tone, label});
  });

  it('puts "for new money" and the thesis state beside the word', () => {
    expect(DossierVerdictUtils.header(scorecard, true, thesis())?.pills).toEqual([
      {label: 'For new money', tone: 'neutral'},
      {label: 'Thesis active', tone: 'success'},
    ]);
    expect(
      DossierVerdictUtils.header(scorecard, true, thesis({brokenAt: '2026-10-02T00:00:00Z'}))
        ?.pills[1]
    ).toEqual({label: 'Thesis invalidated', tone: 'error'});
    expect(DossierVerdictUtils.header(scorecard, false, null)?.pills[1]).toEqual({
      label: 'No thesis on file',
      tone: 'neutral',
    });
  });

  it('carries the fixed held line on a held name, whatever turns it BUY', () => {
    expect(DossierVerdictUtils.header(scorecard, true, null)?.caveat).toBe(
      'You hold this. Holding follows your thesis and its kill-switch, never the word.'
    );
  });

  it('carries what turns the word BUY on a name that is not held', () => {
    expect(DossierVerdictUtils.header(scorecard, false, null)?.caveat).toBe(
      'Turns BUY when crowding is no longer Extended or the P/E premium falls to 0% or below (reads +29.2%).'
    );
  });

  it('has no caveat when a name that is not held has nothing to turn', () => {
    const buy = ScorecardFixtures.scorecard({
      verdict: {...scorecard.verdict, word: 'Buy', turnsBuyWhen: null},
    });

    expect(DossierVerdictUtils.header(buy, false, null)?.caveat).toBeNull();
  });
});
