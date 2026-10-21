import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {of} from 'rxjs';
import {describe, expect, it} from 'vitest';

import {provideErrorMessages} from '../../../../core/providers/error-messages.provider';
import {type NetWorthBridgeResponse} from '../../models/net-worth-bridge/net-worth-bridge.model';
import {NET_WORTH_BRIDGE_WINDOW} from '../../providers/net-worth-bridge-window.provider';
import {BankSyncService} from '../../services/bank-sync.service';
import {NetWorthWhyMovedComponent} from './net-worth-why-moved.component';

const bridge = (overrides: Partial<NetWorthBridgeResponse> = {}): NetWorthBridgeResponse => ({
  from: '2026-07-01',
  to: '2026-10-01',
  startValue: 10_000,
  endValue: 11_000,
  savings: 600,
  investmentReturn: 300,
  market: 300,
  income: 0,
  fees: 0,
  taxes: 0,
  fxTranslation: 100,
  revaluation: 0,
  newlyLinked: 0,
  unlinked: 0,
  residual: 0,
  residualAboveTolerance: false,
  quality: 'COMPLETE',
  reasons: [],
  currency: 'USD',
  ...overrides,
});

function render(response: NetWorthBridgeResponse): HTMLElement {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideErrorMessages(),
      {provide: BankSyncService, useValue: {getNetWorthBridge: () => of(response)}},
      {provide: NET_WORTH_BRIDGE_WINDOW, useValue: signal({from: '2026-07-01', to: '2026-10-01'})},
    ],
  });
  const fixture = TestBed.createComponent(NetWorthWhyMovedComponent);
  fixture.componentRef.setInput('period', 'Last 3 months');
  fixture.componentRef.setInput('detailsParams', {from: '2026-07-01', to: '2026-10-01', months: 3});
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const text = (el: Element | null | undefined): string =>
  el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

describe('NetWorthWhyMovedComponent', () => {
  it('names the window it explains', () => {
    expect(text(render(bridge()).querySelector('h2'))).toBe('Why it moved · Last 3 months');
  });

  it('reads the move as what you saved, markets and exchange rates, then the change', () => {
    const el = render(bridge());
    const amount = (key: string): string =>
      text(
        el.querySelector(
          `[data-testid="net-worth-why-moved-${key}"] [data-testid="net-worth-why-moved-amount"]`
        )
      );

    expect(amount('savings')).toBe('+$600');
    expect(amount('investmentReturn')).toBe('+$300');
    expect(amount('fxTranslation')).toBe('+$100');
    expect(text(el.querySelector('[data-testid="net-worth-why-moved-change"]'))).toBe(
      'Change +$1,000'
    );
    expect(el.querySelector('[data-testid="net-worth-why-moved-change-note"]')).toBeNull();
  });

  it('explains the exchange-rate line in one plain sentence', () => {
    expect(
      text(render(bridge()).querySelector('[data-testid="net-worth-why-moved-fxTranslation"] p'))
    ).toBe(
      'Rates moved, so your accounts in other currencies are worth more in USD, though you did nothing.'
    );
  });

  it('notes what the change holds beyond the three lines', () => {
    const el = render(bridge({endValue: 11_250, newlyLinked: 250}));

    expect(text(el.querySelector('[data-testid="net-worth-why-moved-change-note"]'))).toBe(
      'Includes +$250 from accounts linked or removed, revaluations and moves not yet explained.'
    );
  });

  it('says why there is nothing to explain instead of drawing zeros', () => {
    const el = render(
      bridge({
        startValue: null,
        endValue: null,
        savings: null,
        investmentReturn: null,
        fxTranslation: null,
        residual: null,
        quality: 'UNAVAILABLE',
        reasons: [{code: 'SHORT_HISTORY'}],
      })
    );

    expect(el.querySelector('[data-testid="net-worth-why-moved-unavailable"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="net-worth-why-moved-change"]')).toBeNull();
  });

  it('links to the full statement in Breakdown for the same window', () => {
    const link = render(bridge()).querySelector('[data-testid="net-worth-why-moved-details"]');

    expect(link?.getAttribute('href')).toBe(
      '/dashboard/breakdown?from=2026-07-01&to=2026-10-01&months=3'
    );
  });
});
