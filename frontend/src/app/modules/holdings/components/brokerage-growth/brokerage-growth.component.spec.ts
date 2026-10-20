import {ChangeDetectionStrategy, Component, signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {GROWTH_SLEEVE_COPY} from '../../constants/brokerage-growth/brokerage-growth.constants';
import {
  type BrokerageGrowth,
  type BrokerageGrowthView,
  type GrowthSleeve,
  type GrowthSleeveCopy,
  type GrowthTimingGap,
} from '../../models/brokerage-growth/brokerage-growth.model';
import {BrokerageGrowthService} from '../../services/brokerage-growth.service';
import {BrokerageGrowthStore} from '../../store/brokerage-growth/brokerage-growth.store';
import {BrokerageGrowthComponent} from './brokerage-growth.component';

const GROWTH: BrokerageGrowth = {
  seriesStart: '2026-06-30',
  from: '2026-06-30',
  to: '2026-10-14',
  startValue: 1000,
  endValue: 1720,
  contributed: 100,
  newlyLinked: 510,
  growth: 110,
  income: 15,
  fees: 0,
  taxes: 3,
  moneyWeightedReturn: 0.1,
  sourcesWithoutFlows: [],
  headline: {
    amount: 110,
    percent: 10,
    method: 'MODIFIED_DIETZ',
    scope: 'sleeve:brokerage',
    window: {from: '2026-06-30', to: '2026-10-14'},
    quality: 'COMPLETE',
    reasons: [],
    currency: 'USD',
  },
  currency: 'USD',
};

@Component({
  imports: [BrokerageGrowthComponent],
  template: '<fns-brokerage-growth sleeve="crypto" />',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class CryptoHostComponent {}

describe('BrokerageGrowthComponent', () => {
  let store: {
    sleeve: GrowthSleeve;
    copy: GrowthSleeveCopy;
    timingGap: ReturnType<typeof signal<Nullable<GrowthTimingGap>>>;
    status: ReturnType<typeof signal<AsyncStatus>>;
    errorMessage: ReturnType<typeof signal<string>>;
    growth: ReturnType<typeof signal<Nullable<BrokerageGrowth>>>;
    hasHistory: ReturnType<typeof signal<boolean>>;
    view: ReturnType<typeof signal<BrokerageGrowthView>>;
    returnLabel: ReturnType<typeof signal<Nullable<string>>>;
    showsNewlyLinked: ReturnType<typeof signal<boolean>>;
    missingProviderLabels: ReturnType<typeof signal<string>>;
    isIbkrMissing: ReturnType<typeof signal<boolean>>;
    load: ReturnType<typeof vi.fn>;
  };

  const render = (): HTMLElement => {
    const fixture = TestBed.createComponent(BrokerageGrowthComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (el: HTMLElement, testId: string): string =>
    el.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(async () => {
    store = {
      sleeve: 'brokerage',
      copy: GROWTH_SLEEVE_COPY.brokerage,
      timingGap: signal<Nullable<GrowthTimingGap>>(null),
      status: signal<AsyncStatus>('success'),
      errorMessage: signal(''),
      growth: signal<Nullable<BrokerageGrowth>>(GROWTH),
      hasHistory: signal(true),
      view: signal<BrokerageGrowthView>('split'),
      returnLabel: signal<Nullable<string>>('+10.0%'),
      showsNewlyLinked: signal(true),
      missingProviderLabels: signal(''),
      isIbkrMissing: signal(false),
      load: vi.fn(),
    };
    await TestBed.configureTestingModule({imports: [BrokerageGrowthComponent]})
      .overrideComponent(BrokerageGrowthComponent, {
        set: {providers: [{provide: BrokerageGrowthStore, useValue: store}]},
      })
      .compileComponents();
  });

  it('says the view starts where the value history starts', () => {
    const el = render();

    expect(text(el, 'brokerage-growth-since')).toContain(
      'since 30 Jun 2026 - the day value history starts'
    );
  });

  it('names the window the figures cover once it has moved past the value history start', () => {
    store.growth.set({...GROWTH, from: '2026-07-02'});
    const el = render();

    const since = text(el, 'brokerage-growth-since');
    expect(since).toContain('since 2 Jul 2026');
    expect(since).not.toContain('value history starts');
  });

  it('splits the change into contributions, newly linked value and growth with its return', () => {
    const el = render();

    expect(text(el, 'brokerage-growth-contributed')).toBe('+$100.00');
    expect(text(el, 'brokerage-growth-newly-linked')).toBe('+$510.00');
    expect(text(el, 'brokerage-growth-growth')).toBe('+$110.00 (+10.0%)');
  });

  it('leaves the newly linked line out when nothing was linked', () => {
    store.showsNewlyLinked.set(false);

    const el = render();

    expect(el.querySelector('[data-testid="brokerage-growth-newly-linked"]')).toBeNull();
  });

  it('says no contributions are recorded yet, with the IBKR Flex hint, instead of a split', () => {
    store.view.set('no-flows');
    store.missingProviderLabels.set('Interactive Brokers');
    store.isIbkrMissing.set(true);

    const el = render();

    expect(el.querySelector('[data-testid="brokerage-growth-split"]')).toBeNull();
    const note = text(el, 'brokerage-growth-no-flows');
    expect(note).toContain('No contributions recorded yet for Interactive Brokers.');
    expect(note).toContain('Deposits & Withdrawals');
  });

  it('leaves the Flex hint out when IBKR has reported', () => {
    store.view.set('no-flows');
    store.missingProviderLabels.set('Inzhur');

    const el = render();

    expect(text(el, 'brokerage-growth-no-flows')).not.toContain('Flex Query');
  });

  it('renders nothing for a user with no brokerage value history', () => {
    store.hasHistory.set(false);

    const el = render();

    expect(el.querySelector('[data-testid="brokerage-growth"]')).toBeNull();
  });

  it('says the timing gap between the investments and the money', () => {
    store.timingGap.set({investments: '0.0%', money: '+5.3%', points: '5.3'});

    const el = render();

    expect(text(el, 'brokerage-growth-timing-gap')).toBe(
      'Your investments returned 0.0% (time-weighted); your money earned +5.3% (money-weighted). ' +
        'The difference, 5.3 points, is the timing of your deposits and withdrawals.'
    );
  });

  it('leaves the timing gap out when it is not measured', () => {
    const el = render();

    expect(el.querySelector('[data-testid="brokerage-growth-timing-gap"]')).toBeNull();
  });

  it('reads the crypto sleeve when its host says so', () => {
    const getGrowth = vi
      .fn()
      .mockReturnValue(of({...GROWTH, headline: {...GROWTH.headline, scope: 'sleeve:crypto'}}));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [CryptoHostComponent],
      providers: [{provide: BrokerageGrowthService, useValue: {getGrowth}}],
    });
    const fixture = TestBed.createComponent(CryptoHostComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(getGrowth).toHaveBeenCalledWith('crypto');
    expect(text(el, 'crypto-growth-since')).toContain('Binance and Revolut X since 30 Jun 2026');
    expect(el.querySelector('[data-testid="brokerage-growth"]')).toBeNull();
  });
});
