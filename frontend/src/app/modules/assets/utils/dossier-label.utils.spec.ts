import {DossierLabelUtils} from './dossier-label.utils';

describe('DossierLabelUtils', () => {
  it('maps known coverage codes', () => {
    expect(DossierLabelUtils.coverage('inUniverse')).toBe('In your research universe');
    expect(DossierLabelUtils.coverage('notInUniverse')).toBe('Outside your research universe');
  });

  it('title-cases unknown camelCase coverage codes', () => {
    expect(DossierLabelUtils.coverage('partiallyCovered')).toBe('Partially Covered');
  });

  it('maps known signal types', () => {
    expect(DossierLabelUtils.signalType('RSI_OVERSOLD')).toBe('RSI oversold');
    expect(DossierLabelUtils.signalType('VOLUME_SPIKE')).toBe('Volume spike');
  });

  it('title-cases unknown snake_case signal types', () => {
    expect(DossierLabelUtils.signalType('NEW_BREAKOUT_x')).toBe('New Breakout X');
  });

  it('handles an empty code', () => {
    expect(DossierLabelUtils.humanize('')).toBe('');
  });
});
