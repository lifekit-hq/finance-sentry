import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type SleeveCosts} from '../../models/cost-of-investing/cost-of-investing.model';
import {CostOfInvestingStore} from '../../store/cost-of-investing/cost-of-investing.store';
import {CostOfInvestingComponent} from './cost-of-investing.component';

const COSTS: SleeveCosts = {
  asOf: '2026-11-24',
  yearStart: '2026-01-01',
  commissions: 30,
  fees: 10,
  fxConversion: 5,
  withholding: 40,
  total: 85,
  shareOfReturns: 0.085,
  unvaluedRows: 0,
  approximateRows: 0,
  fxReasons: [],
  currency: 'USD',
};

describe('CostOfInvestingComponent', () => {
  let costs: ReturnType<typeof signal<SleeveCosts>>;
  let hasCosts: ReturnType<typeof signal<boolean>>;
  let sleeve: 'brokerage' | 'crypto';

  const render = (): HTMLElement => {
    const fixture = TestBed.createComponent(CostOfInvestingComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (el: HTMLElement, testId: string): string =>
    el.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  const configure = async (): Promise<void> => {
    const store = {
      sleeve,
      status: signal<AsyncStatus>('success'),
      errorMessage: signal(''),
      costs,
      hasCosts,
      parts: signal(
        [
          {key: 'commissions', label: 'Commissions', amount: costs().commissions},
          {key: 'fees', label: 'Fees', amount: costs().fees},
          {key: 'fx', label: 'FX conversion', amount: costs().fxConversion},
          {key: 'withholding', label: 'Withholding tax', amount: costs().withholding},
        ].filter(part => part.amount !== 0)
      ),
      shareLabel: signal<string | null>('8.5%'),
      load: vi.fn(),
    };
    await TestBed.configureTestingModule({imports: [CostOfInvestingComponent]})
      .overrideComponent(CostOfInvestingComponent, {
        set: {providers: [{provide: CostOfInvestingStore, useValue: store}]},
      })
      .compileComponents();
  };

  beforeEach(() => {
    costs = signal(COSTS);
    hasCosts = signal(true);
    sleeve = 'brokerage';
  });

  it('shows one row with the total, its share of returns and every part', async () => {
    await configure();
    const el = render();

    expect(text(el, 'brokerage-cost-total')).toContain('$85.00');
    expect(text(el, 'brokerage-cost-share')).toBe('8.5% of returns');
    expect(text(el, 'brokerage-cost-parts')).toBe(
      'Year to date: Commissions $30.00 · Fees $10.00 · FX conversion $5.00 · Withholding tax $40.00'
    );
  });

  it('lists only the parts that were paid', async () => {
    costs.set({...COSTS, fees: 0, fxConversion: 0, withholding: 0, commissions: 20, total: 20});
    await configure();
    const el = render();

    expect(text(el, 'brokerage-cost-parts')).toBe('Year to date: Commissions $20.00');
  });

  it('keys the crypto sleeve by its own test ids', async () => {
    sleeve = 'crypto';
    await configure();

    expect(text(render(), 'crypto-cost-total')).toContain('$85.00');
  });

  it('says how many rows are approximate or unvalued, and nothing when none are', async () => {
    await configure();
    expect(text(render(), 'brokerage-cost-approximate')).toBe('');
    expect(text(render(), 'brokerage-cost-unvalued')).toBe('');

    costs.set({...COSTS, approximateRows: 2, unvaluedRows: 1});
    const el = render();

    expect(text(el, 'brokerage-cost-approximate')).toBe(
      'Approximate: 2 cost rows were converted without a published rate.'
    );
    expect(text(el, 'brokerage-cost-unvalued')).toBe(
      '1 fees are not counted: no USD value was recorded.'
    );
  });

  it('renders no row for a book with no costs', async () => {
    hasCosts.set(false);
    await configure();

    expect(render().querySelector('[data-testid="brokerage-cost-of-investing"]')).toBeNull();
  });
});
