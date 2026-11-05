import {type ComponentFixture, TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it} from 'vitest';

import {type DisposalTaxContextDto} from '../../models/dossier/dossier.model';
import {DisposalTaxComponent} from './disposal-tax.component';

const DISCLAIMER = 'Context for your own planning, not tax advice or a tax return.';

const SHOWN: DisposalTaxContextDto = {
  symbol: 'ABCD',
  status: 'shown',
  withheldReasons: [],
  regime: 'cgt',
  taxYear: 2026,
  ratesCarriedForward: false,
  currency: 'EUR',
  rate: 0.33,
  annualExemptionEur: 1270,
  proceedsEur: 2500,
  costEur: 1900,
  gainEur: 600,
  estimatedTaxEur: 198,
  taxAfterExemptionEur: 0,
  deemedDisposalYears: null,
  nextDeemedDisposalOn: null,
  lots: [
    {
      acquiredOn: '2024-01-10',
      quantity: 5,
      costEur: 400,
      proceedsEur: 833.33,
      gainEur: 433.33,
      deemedDisposalOn: null,
    },
    {
      acquiredOn: '2025-03-03',
      quantity: 10,
      costEur: 1500,
      proceedsEur: 1666.67,
      gainEur: 166.67,
      deemedDisposalOn: null,
    },
  ],
  method: 'Whole position sold today.',
  ratesSource: 'revenue.ie',
  disclaimer: DISCLAIMER,
};

const WITHHELD: DisposalTaxContextDto = {
  ...SHOWN,
  status: 'withheld',
  withheldReasons: ['UNCLASSIFIED', 'BASIS_NOT_VERIFIED'],
  regime: null,
  taxYear: null,
  rate: null,
  annualExemptionEur: null,
  proceedsEur: null,
  costEur: null,
  gainEur: null,
  estimatedTaxEur: null,
  taxAfterExemptionEur: null,
  lots: [],
};

describe('DisposalTaxComponent', () => {
  let fixture: ComponentFixture<DisposalTaxComponent>;

  const render = (context: DisposalTaxContextDto): HTMLElement => {
    fixture = TestBed.createComponent(DisposalTaxComponent);
    fixture.componentRef.setInput('context', context);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };
  const text = (root: HTMLElement, testId: string): string =>
    root.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(() => {
    TestBed.configureTestingModule({imports: [DisposalTaxComponent]});
  });

  it('shows the regime, rate, euro figures and every lot for a CGT estimate', () => {
    const root = render(SHOWN);

    expect(text(root, 'disposal-tax-regime')).toBe('Capital gains tax');
    expect(text(root, 'disposal-tax-rate')).toBe('33% · 2026 rates');
    expect(text(root, 'disposal-tax-amount')).toContain('198');
    expect(text(root, 'disposal-tax-amount')).toContain('€');
    expect(text(root, 'disposal-tax-gain')).toContain('600');
    expect(root.querySelectorAll('[data-testid="disposal-tax-lots"] > div').length).toBe(2);
    expect(text(root, 'disposal-tax-exemption')).toContain('annual exemption');
    expect(root.querySelector('[data-testid="disposal-tax-deemed"]')).toBeNull();
    expect(text(root, 'disposal-tax-disclaimer')).toBe(DISCLAIMER);
  });

  it('names the next deemed disposal for an exit-tax estimate and shows no exemption', () => {
    const root = render({
      ...SHOWN,
      regime: 'exitTax',
      rate: 0.38,
      annualExemptionEur: null,
      taxAfterExemptionEur: null,
      deemedDisposalYears: 8,
      nextDeemedDisposalOn: '2031-06-01',
    });

    expect(text(root, 'disposal-tax-regime')).toBe('Exit tax');
    expect(text(root, 'disposal-tax-rate')).toBe('38% · 2026 rates');
    expect(text(root, 'disposal-tax-deemed')).toContain('Jun 1, 2031');
    expect(root.querySelector('[data-testid="disposal-tax-exemption"]')).toBeNull();
  });

  it('says the rates are carried forward when the year has no reviewed row', () => {
    const root = render({...SHOWN, ratesCarriedForward: true});

    expect(text(root, 'disposal-tax-carried')).toContain('latest reviewed');
  });

  it('withholds without any figure and lists why', () => {
    const root = render(WITHHELD);

    expect(text(root, 'disposal-tax-withheld')).toContain('Not enough data to estimate');
    expect(text(root, 'disposal-tax-withheld')).toContain('no tax classification');
    expect(text(root, 'disposal-tax-withheld')).toContain('cost basis is not verified');
    expect(root.querySelector('[data-testid="disposal-tax-amount"]')).toBeNull();
    expect(text(root, 'disposal-tax-disclaimer')).toBe(DISCLAIMER);
  });
});
