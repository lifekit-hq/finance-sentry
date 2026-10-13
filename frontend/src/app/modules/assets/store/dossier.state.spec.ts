import {describe, expect, it} from 'vitest';

import {initialDossierState} from './dossier.state';

describe('initialDossierState', () => {
  it('starts with no dossier loaded', () => {
    expect(initialDossierState.dossier).toBeNull();
  });

  it("starts with no Ledger's read loaded", () => {
    expect(initialDossierState.ledgerRead).toBeNull();
  });

  it('starts with the thesis collapsed', () => {
    expect(initialDossierState.isThesisExpanded).toBe(false);
  });

  it('starts with no quote', () => {
    expect(initialDossierState.quote).toBeNull();
  });

  it('starts on the one-year chart with the target editor closed', () => {
    expect(initialDossierState.chart).toBeNull();
    expect(initialDossierState.chartRange).toBe('1Y');
    expect(initialDossierState.isEditingTarget).toBe(false);
    expect(initialDossierState.targetDraft).toBe('');
  });

  it('starts with no decided proposal', () => {
    expect(initialDossierState.decidedRecommendation).toBeNull();
  });
});
