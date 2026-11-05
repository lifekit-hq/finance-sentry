import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {ASYNC_STATE_SHOW_DELAY_MS} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it, onTestFinished, vi} from 'vitest';

import {type PnlRow} from '../../models/brokerage-pnl/brokerage-pnl.model';
import {BrokeragePnlStore} from '../../store/brokerage-pnl/brokerage-pnl.store';
import {BrokeragePnlComponent} from './brokerage-pnl.component';

const ROWS: PnlRow[] = [
  {
    key: 'net-deposited',
    label: 'Capital in the window',
    amount: 1500,
    percentLabel: null,
    note: 'Opening value + deposits less withdrawals',
    detail: null,
    unavailable: null,
    tone: 'neutral',
    signed: false,
  },
  {
    key: 'realized',
    label: 'Realized P&L',
    amount: 150,
    percentLabel: '+10.0%',
    note: 'From your fills',
    detail: null,
    unavailable: null,
    tone: 'gain',
    signed: true,
  },
  {
    key: 'unrealized',
    label: 'Unrealized P&L',
    amount: null,
    percentLabel: null,
    note: 'No verified cost basis',
    detail: 'Unrealized P&L is not stated: no position has a cost basis your fills verify.',
    unavailable: 'Basis unavailable',
    tone: 'neutral',
    signed: true,
  },
];

describe('BrokeragePnlComponent', () => {
  let store: {
    status: ReturnType<typeof signal<AsyncStatus>>;
    errorMessage: ReturnType<typeof signal<string>>;
    rows: ReturnType<typeof signal<PnlRow[]>>;
    currency: ReturnType<typeof signal<string>>;
    load: ReturnType<typeof vi.fn>;
  };

  const render = (): HTMLElement => {
    const fixture = TestBed.createComponent(BrokeragePnlComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (el: HTMLElement, testId: string): string =>
    el.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(async () => {
    store = {
      status: signal<AsyncStatus>('success'),
      errorMessage: signal(''),
      rows: signal(ROWS),
      currency: signal('USD'),
      load: vi.fn(),
    };
    await TestBed.configureTestingModule({imports: [BrokeragePnlComponent]})
      .overrideComponent(BrokeragePnlComponent, {
        set: {providers: [{provide: BrokeragePnlStore, useValue: store}]},
      })
      .compileComponents();
  });

  it('shows the capital in the window, a signed gain with its percent, and the reason a figure is missing', () => {
    const el = render();

    expect(text(el, 'brokerage-pnl-net-deposited-amount')).toBe('$1,500.00');
    expect(text(el, 'brokerage-pnl-realized-amount')).toBe('+$150.00 +10.0%');
    expect(text(el, 'brokerage-pnl-unrealized-amount')).toBe('Basis unavailable');
    expect(el.querySelector('[data-testid="brokerage-pnl-unrealized"]')?.textContent).toContain(
      'No verified cost basis'
    );
    expect(text(el, 'brokerage-pnl-unrealized-detail')).toBe(
      'Unrealized P&L is not stated: no position has a cost basis your fills verify.'
    );
    expect(el.querySelector('[data-testid="brokerage-pnl-realized-detail"]')).toBeNull();
  });

  it('shows a placeholder while loading', () => {
    vi.useFakeTimers();
    onTestFinished(() => {
      vi.useRealTimers();
    });
    store.status.set('loading');
    const fixture = TestBed.createComponent(BrokeragePnlComponent);
    fixture.detectChanges();
    vi.advanceTimersByTime(ASYNC_STATE_SHOW_DELAY_MS);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('cmn-skeleton')).not.toBeNull();
    expect(el.querySelector('[data-testid="brokerage-pnl"]')).toBeNull();
  });

  it('says the request failed and retries it', () => {
    store.status.set('error');
    store.errorMessage.set('Failed to load realized and unrealized P&L.');
    store.rows.set([]);

    const el = render();

    expect(el.textContent).toContain('Failed to load realized and unrealized P&L.');
    Array.from(el.querySelectorAll('button'))
      .find(b => b.textContent?.includes('Retry'))
      ?.click();
    expect(store.load).toHaveBeenCalledTimes(1);
  });
});
