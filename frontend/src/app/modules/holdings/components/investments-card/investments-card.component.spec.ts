import {Component, signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type AllocationBreakdownRow, type PositionRow} from '../../store/holdings.computed';
import {HoldingsStore} from '../../store/holdings.store';
import {BrokerageGrowthComponent} from '../brokerage-growth/brokerage-growth.component';
import {GrowthOfTenThousandComponent} from '../growth-of-ten-thousand/growth-of-ten-thousand.component';
import {InvestmentsCardComponent} from './investments-card.component';

@Component({selector: 'fns-brokerage-growth', template: '<span data-testid="growth-stub"></span>'})
class BrokerageGrowthStubComponent {}

@Component({
  selector: 'fns-growth-of-ten-thousand',
  template: '<span data-testid="ten-thousand-stub"></span>',
})
class GrowthOfTenThousandStubComponent {}

function row(
  symbol: string,
  provider: string,
  currentValue: number,
  weightPercent: number
): PositionRow {
  return {
    symbol,
    provider,
    providerLabel: provider,
    assetClass: 'equity',
    quantity: 1,
    currentPrice: currentValue,
    currentValue,
    pnlPercent: null,
    pnlUsd: null,
    dayChangePct: null,
    dayChangeUsd: null,
    hiddenReason: null,
    weightPercent,
  };
}

const ALLOCATION: AllocationBreakdownRow[] = [
  {label: 'Equities', color: 'rgb(1, 2, 3)', value: 40_000, percent: 83},
  {label: 'Crypto', color: 'rgb(4, 5, 6)', value: 8_210, percent: 17},
];

describe('InvestmentsCardComponent', () => {
  let store: Record<string, unknown>;

  const render = (): HTMLElement => {
    const fixture = TestBed.createComponent(InvestmentsCardComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const byId = (el: HTMLElement, id: string) => el.querySelector(`[data-testid="${id}"]`);

  beforeEach(async () => {
    store = {
      errorMessage: signal(''),
      isLoading: signal(false),
      isEmpty: signal(false),
      isTotalUnavailable: signal(false),
      totalPositionsValue: signal(53_210),
      investedPositionsValue: signal(48_210),
      totalHeadline: signal(null),
      allocationBreakdown: signal(ALLOCATION),
      allocationSegments: signal([]),
      topHoldings: signal([row('VWCE', 'ibkr', 30_000, 62.2), row('BTC', 'binance', 8_210, 17)]),
      loadPositions: vi.fn(),
    };
    await TestBed.configureTestingModule({imports: [InvestmentsCardComponent]})
      .overrideComponent(InvestmentsCardComponent, {
        remove: {imports: [BrokerageGrowthComponent, GrowthOfTenThousandComponent]},
        add: {imports: [BrokerageGrowthStubComponent, GrowthOfTenThousandStubComponent]},
      })
      .overrideComponent(InvestmentsCardComponent, {
        set: {providers: [{provide: HoldingsStore, useValue: store}]},
      })
      .compileComponents();
    TestBed.configureTestingModule({providers: [provideRouter([])]});
  });

  it('heads the card with the invested total', () => {
    const el = render();

    expect(byId(el, 'investments-card-total')?.textContent?.trim()).toBe('$48,210.00');
    expect(el.querySelector('h2')?.textContent?.replace(/\s+/g, ' ').trim()).toMatch(
      /^Invested \$48,210/
    );
    expect(byId(el, 'investments-card-quality')).not.toBeNull();
  });

  it('reads N/A, never zero, when the total is unavailable', () => {
    (store['isTotalUnavailable'] as ReturnType<typeof signal<boolean>>).set(true);

    expect(byId(render(), 'investments-card-total')?.textContent?.trim()).toBe('N/A');
  });

  it('lists the allocation rows with their percent and value', () => {
    const rows = byId(render(), 'investments-card-allocation')?.textContent ?? '';

    expect(rows).toContain('Equities');
    expect(rows).toContain('83%');
    expect(rows).toContain('$8,210');
  });

  it('shows the brokerage and crypto growth sections and the growth of 10,000', () => {
    const el = render();

    expect(el.querySelectorAll('[data-testid="growth-stub"]')).toHaveLength(2);
    expect(byId(el, 'ten-thousand-stub')).not.toBeNull();
  });

  it('lists the top holdings, each linking to its dossier', () => {
    const el = render();
    const links = Array.from(
      el.querySelectorAll<HTMLAnchorElement>('[data-testid="investments-card-holding-link"]')
    );

    expect(links.map(a => a.getAttribute('href'))).toEqual(['/assets/VWCE', '/assets/BTC']);
    expect(byId(el, 'investments-card-top-holdings')?.textContent).toContain('62.2%');
  });

  it('holds a fixed-height skeleton while loading', () => {
    (store['isLoading'] as ReturnType<typeof signal<boolean>>).set(true);
    const el = render();

    expect(byId(el, 'investments-card-skeleton')?.className).toContain('h-[80rem]');
    expect(byId(el, 'investments-card-total')).toBeNull();
  });

  it('renders nothing for a settled book without positions', () => {
    (store['isEmpty'] as ReturnType<typeof signal<boolean>>).set(true);

    expect(byId(render(), 'investments-card')).toBeNull();
  });

  it('keeps the card with a retry when the positions fail to load', () => {
    (store['errorMessage'] as ReturnType<typeof signal<string>>).set('Failed to load positions.');
    (store['isEmpty'] as ReturnType<typeof signal<boolean>>).set(true);
    const el = render();

    expect(byId(el, 'investments-card')).not.toBeNull();
    expect(el.textContent).toContain('Failed to load positions.');
  });
});
