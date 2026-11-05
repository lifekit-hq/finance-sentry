import {describe, expect, it} from 'vitest';

import {DisposalTaxUtils} from './disposal-tax.utils';

describe('DisposalTaxUtils', () => {
  it('reasonText maps a known code to its sentence', () => {
    expect(DisposalTaxUtils.reasonText('UNCLASSIFIED')).toContain('no tax classification');
  });

  it('reasonText falls back for an unknown code', () => {
    expect(DisposalTaxUtils.reasonText('SOMETHING_NEW')).toBe(
      'The data does not support an estimate.'
    );
  });

  it('ratePercent turns a fraction into a percent', () => {
    expect(DisposalTaxUtils.ratePercent(0.33)).toBe(33);
    expect(DisposalTaxUtils.ratePercent(0.385)).toBe(38.5);
  });

  it('ratePercent is null for a missing rate', () => {
    expect(DisposalTaxUtils.ratePercent(null)).toBeNull();
    expect(DisposalTaxUtils.ratePercent(undefined)).toBeNull();
  });
});
