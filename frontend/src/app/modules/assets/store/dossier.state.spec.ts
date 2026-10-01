import {describe, expect, it} from 'vitest';

import {initialDossierState} from './dossier.state';

describe('initialDossierState', () => {
  it('starts with no dossier loaded', () => {
    expect(initialDossierState.dossier).toBeNull();
  });

  it('starts in idle status', () => {
    expect(initialDossierState.dossierStatus).toBe('idle');
  });

  it('starts with no error code', () => {
    expect(initialDossierState.dossierErrorCode).toBeNull();
  });

  it("starts with no Ledger's read loaded", () => {
    expect(initialDossierState.ledgerRead).toBeNull();
    expect(initialDossierState.ledgerReadStatus).toBe('idle');
    expect(initialDossierState.ledgerReadErrorCode).toBeNull();
  });

  it('starts with the thesis collapsed', () => {
    expect(initialDossierState.isThesisExpanded).toBe(false);
  });
});
