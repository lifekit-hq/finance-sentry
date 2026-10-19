import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type Headline} from '../../../../shared/models/headline/headline.model';
import {
  type NetWorthBridgeDegraded,
  type NetWorthBridgeResponse,
  type NetWorthBridgeStep,
} from '../../models/net-worth-bridge/net-worth-bridge.model';
import {NetWorthBridgeStore} from '../../store/net-worth-bridge/net-worth-bridge.store';
import {NetWorthBridgeUtils} from '../../utils/net-worth-bridge.utils';
import {NetWorthBridgeComponent} from './net-worth-bridge.component';

const BRIDGE: NetWorthBridgeResponse = {
  from: '2026-09-01',
  to: '2026-10-01',
  startValue: 10_000,
  endValue: 11_250,
  savings: 600,
  investmentReturn: 300,
  market: 290,
  income: 40,
  fees: 30,
  taxes: 0,
  fxTranslation: 100,
  revaluation: 0,
  newlyLinked: 0,
  unlinked: 0,
  residual: 250,
  residualAboveTolerance: true,
  quality: 'DEGRADED',
  reasons: [{code: 'RESIDUAL_ABOVE_TOLERANCE'}],
  currency: 'USD',
};

const DEGRADED: NetWorthBridgeDegraded = {
  reason: 'The change in net worth is not fully explained by known movements and price changes.',
  detail: '+$250.00 is unexplained.',
};

describe('NetWorthBridgeComponent', () => {
  let store: {
    status: ReturnType<typeof signal<AsyncStatus>>;
    errorMessage: ReturnType<typeof signal<string>>;
    steps: ReturnType<typeof signal<NetWorthBridgeStep[]>>;
    headline: ReturnType<typeof signal<Nullable<Headline>>>;
    unavailableMessage: ReturnType<typeof signal<Nullable<string>>>;
    degraded: ReturnType<typeof signal<Nullable<NetWorthBridgeDegraded>>>;
    retry: ReturnType<typeof vi.fn>;
  };

  const render = (): HTMLElement => {
    const fixture = TestBed.createComponent(NetWorthBridgeComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const byTestId = (el: HTMLElement, testId: string): Nullable<Element> =>
    el.querySelector(`[data-testid="${testId}"]`);

  const text = (el: Nullable<Element>): string =>
    el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(async () => {
    store = {
      status: signal<AsyncStatus>('success'),
      errorMessage: signal(''),
      steps: signal(NetWorthBridgeUtils.steps(BRIDGE)),
      headline: signal<Nullable<Headline>>(NetWorthBridgeUtils.headline(BRIDGE)),
      unavailableMessage: signal<Nullable<string>>(null),
      degraded: signal<Nullable<NetWorthBridgeDegraded>>(DEGRADED),
      retry: vi.fn(),
    };
    await TestBed.configureTestingModule({imports: [NetWorthBridgeComponent]})
      .overrideComponent(NetWorthBridgeComponent, {
        set: {providers: [{provide: NetWorthBridgeStore, useValue: store}]},
      })
      .compileComponents();
  });

  it('walks from the start to the end through each line, signed', () => {
    const el = render();

    expect(text(byTestId(el, 'net-worth-bridge-window'))).toBe('1 Sep 2026 to 1 Oct 2026');
    expect(text(byTestId(el, 'net-worth-bridge-start'))).toContain('$10,000.00');
    expect(text(byTestId(el, 'net-worth-bridge-savings'))).toContain('+$600.00');
    expect(text(byTestId(el, 'net-worth-bridge-investmentReturn'))).toContain(
      'market +$290.00 · income +$40.00 · fees -$30.00'
    );
    expect(text(byTestId(el, 'net-worth-bridge-fxTranslation'))).toContain('+$100.00');
    expect(text(byTestId(el, 'net-worth-bridge-end'))).toContain('$11,250.00');
  });

  it('shows the residual as its own row in the warning colour, with the DEGRADED reason in place', () => {
    const el = render();
    const residual = byTestId(el, 'net-worth-bridge-residual');

    expect(text(residual)).toContain('Unexplained');
    expect(text(residual)).toContain('+$250.00');
    expect(residual?.querySelector('[data-testid="net-worth-bridge-amount"]')?.className).toContain(
      'text-status-warning'
    );
    expect(text(byTestId(el, 'net-worth-bridge-degraded'))).toContain(DEGRADED.reason);
    expect(text(byTestId(el, 'net-worth-bridge-degraded'))).toContain(DEGRADED.detail);
    expect(byTestId(el, 'net-worth-bridge-quality')).not.toBeNull();
  });

  it('keeps a zero residual on screen, muted, with no warning', () => {
    const balanced = {...BRIDGE, endValue: 11_000, residual: 0, residualAboveTolerance: false};
    store.steps.set(NetWorthBridgeUtils.steps(balanced));
    store.degraded.set(null);
    const el = render();
    const residual = byTestId(el, 'net-worth-bridge-residual');

    expect(text(residual)).toContain('$0.00');
    expect(residual?.querySelector('[data-testid="net-worth-bridge-amount"]')?.className).toContain(
      'text-text-secondary'
    );
    expect(byTestId(el, 'net-worth-bridge-degraded')).toBeNull();
  });

  it('says why there is no bridge instead of drawing one', () => {
    store.steps.set([]);
    store.degraded.set(null);
    store.unavailableMessage.set('There is not enough history yet to measure this period.');
    const el = render();

    expect(text(byTestId(el, 'net-worth-bridge-unavailable'))).toBe(
      'There is not enough history yet to measure this period.'
    );
    expect(el.querySelector('li')).toBeNull();
  });

  it('retries a failed load', () => {
    store.status.set('error');
    store.errorMessage.set('Failed to load why your net worth changed.');
    const el = render();

    const retry = Array.from(el.querySelectorAll('button')).find(b =>
      /try again|retry/i.test(b.textContent ?? '')
    );
    retry?.click();

    expect(el.textContent).toContain('Failed to load why your net worth changed.');
    expect(store.retry).toHaveBeenCalledOnce();
  });
});
