import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type GrowthOfTenThousand} from '../../models/book-performance/book-performance.model';
import {BookPerformanceStore} from '../../store/book-performance/book-performance.store';
import {GrowthOfTenThousandComponent} from './growth-of-ten-thousand.component';

const GROWTH: GrowthOfTenThousand = {
  from: '2025-10-19',
  to: '2026-10-19',
  bookValue: 11200,
  benchmarkValue: 11000,
  bookReturn: '+12.0%',
  benchmarkReturn: '+10.0%',
  headline: {
    amount: null,
    percent: 12,
    method: 'TWR',
    scope: 'sleeve:brokerage',
    window: {from: '2025-10-19', to: '2026-10-19'},
    quality: 'ESTIMATED',
    reasons: [{code: 'UNKNOWN_FLOW', source: 'ibkr'}],
    currency: 'USD',
  },
};

describe('GrowthOfTenThousandComponent', () => {
  let store: {
    status: ReturnType<typeof signal<AsyncStatus>>;
    errorMessage: ReturnType<typeof signal<string>>;
    growth: ReturnType<typeof signal<Nullable<GrowthOfTenThousand>>>;
    load: ReturnType<typeof vi.fn>;
  };

  const render = (): HTMLElement => {
    const fixture = TestBed.createComponent(GrowthOfTenThousandComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (el: HTMLElement, testId: string): string =>
    el.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(async () => {
    store = {
      status: signal<AsyncStatus>('success'),
      errorMessage: signal(''),
      growth: signal<Nullable<GrowthOfTenThousand>>(GROWTH),
      load: vi.fn(),
    };
    await TestBed.configureTestingModule({imports: [GrowthOfTenThousandComponent]})
      .overrideComponent(GrowthOfTenThousandComponent, {
        set: {providers: [{provide: BookPerformanceStore, useValue: store}]},
      })
      .compileComponents();
  });

  it('shows what 10,000 in the book and in SPY would be worth now', () => {
    const el = render();

    expect(el.textContent).toContain('Growth of $10,000');
    expect(text(el, 'growth-of-ten-thousand-since')).toBe(
      'Put in on 19 Oct 2025, worth on 19 Oct 2026.'
    );
    expect(text(el, 'growth-of-ten-thousand-book')).toBe('$11,200.00 (+12.0%)');
    expect(text(el, 'growth-of-ten-thousand-spy')).toBe('$11,000.00 (+10.0%)');
  });

  it('wears the book return badge', () => {
    const el = render();

    expect(el.querySelector('[data-testid="growth-of-ten-thousand-quality"]')).not.toBeNull();
  });

  it('wears no badge without a headline', () => {
    store.growth.set({...GROWTH, headline: null});

    const el = render();

    expect(el.querySelector('[data-testid="growth-of-ten-thousand-quality"]')).toBeNull();
  });

  it('renders nothing once loaded with no window measured for both', () => {
    store.growth.set(null);

    const el = render();

    expect(el.querySelector('[data-testid="growth-of-ten-thousand"]')).toBeNull();
  });

  it('keeps its place while loading', () => {
    store.status.set('loading');
    store.growth.set(null);

    const el = render();

    expect(el.querySelector('[data-testid="growth-of-ten-thousand"]')).not.toBeNull();
  });
});
