import {describe, expect, it} from 'vitest';

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
