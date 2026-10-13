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

  it('maps known analyst actions to plain words', () => {
    expect(DossierLabelUtils.analystAction('TargetChange')).toBe('Target changed');
    expect(DossierLabelUtils.analystAction('Initiate')).toBe('Initiated');
    expect(DossierLabelUtils.analystAction('Reiterate')).toBe('Reiterated');
    expect(DossierLabelUtils.analystAction('TopIdea')).toBe('Top idea');
  });

  it('matches an analyst action regardless of casing', () => {
    expect(DossierLabelUtils.analystAction('targetchange')).toBe('Target changed');
  });

  it('title-cases an analyst action the map does not know', () => {
    expect(DossierLabelUtils.analystAction('PriceCut')).toBe('Price Cut');
  });

  it('names the provider behind a Yahoo ticker feed', () => {
    expect(DossierLabelUtils.newsSource('yahoo:GRAB')).toBe('Yahoo Finance');
  });

  it('names a known feed label', () => {
    expect(DossierLabelUtils.newsSource('fed-press')).toBe('Federal Reserve');
  });

  it('leaves a publisher name untouched', () => {
    expect(DossierLabelUtils.newsSource(' CNBC ')).toBe('CNBC');
  });

  it('turns a sector peer set into words', () => {
    expect(DossierLabelUtils.peerSet('sector:Technology (default)')).toBe('Technology peers');
  });

  it('falls back to similar companies for a sector peer set without a sector', () => {
    expect(DossierLabelUtils.peerSet('sector: (default)')).toBe('Similar companies');
  });

  it('names the default and custom peer sets', () => {
    expect(DossierLabelUtils.peerSet('default')).toBe('Similar companies');
    expect(DossierLabelUtils.peerSet('custom')).toBe('Your peer set');
  });

  it('passes an unrecognised peer set name through', () => {
    expect(DossierLabelUtils.peerSet('Semis')).toBe('Semis');
  });

  it('handles an empty code', () => {
    expect(DossierLabelUtils.humanize('')).toBe('');
  });
});
