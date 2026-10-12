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
});
