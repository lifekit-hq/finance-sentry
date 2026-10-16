import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it} from 'vitest';

import {dashboardComputed} from './dashboard.computed';
import {build, snapshot} from './dashboard.computed.fixtures';

function projection(history = [snapshot(1, 0, 0)]) {
  const signals = build({monthlyFlow: [], netWorthHistory: history}, '3m');
  const computed = TestBed.runInInjectionContext(() => dashboardComputed(signals));
  return {signals, computed};
}

describe('dashboardComputed first-load skeletons', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideApiBaseUrl('http://localhost/api/v1'),
      ],
    });
  });

  it('keeps the numbers while the same range refreshes', () => {
    const {signals, computed} = projection();

    signals.isLoading.set(true);
    signals.historyLoading.set(true);

    expect(computed.isFirstLoad()).toBe(false);
    expect(computed.isHistoryFirstLoad()).toBe(false);
  });

  it('shows the skeletons while another range loads', () => {
    const {signals, computed} = projection();

    signals.historyRange.set('1y');
    signals.isLoading.set(true);
    signals.historyLoading.set(true);

    expect(computed.isFirstLoad()).toBe(true);
    expect(computed.isHistoryFirstLoad()).toBe(true);
  });

  it('keeps the skeletons when the load for another range failed', () => {
    const {signals, computed} = projection();

    signals.historyRange.set('1y');

    expect(computed.isFirstLoad()).toBe(true);
    expect(computed.isHistoryFirstLoad()).toBe(true);
  });

  it('shows the numbers again once the new range lands', () => {
    const {signals, computed} = projection();

    signals.historyRange.set('1y');
    signals.dataRange.set('1y');
    signals.historyLoadedRange.set('1y');

    expect(computed.isFirstLoad()).toBe(false);
    expect(computed.isHistoryFirstLoad()).toBe(false);
  });

  it('shows the skeletons while there is nothing to keep', () => {
    const {signals, computed} = projection([]);

    signals.data.set(null);
    signals.dataRange.set(null);
    signals.historyLoadedRange.set(null);
    signals.isLoading.set(true);
    signals.historyLoading.set(true);

    expect(computed.isFirstLoad()).toBe(true);
    expect(computed.isHistoryFirstLoad()).toBe(true);
  });

  it('shows no skeleton before any load starts', () => {
    const {signals, computed} = projection([]);

    signals.data.set(null);
    signals.dataRange.set(null);

    expect(computed.isFirstLoad()).toBe(false);
    expect(computed.isHistoryFirstLoad()).toBe(false);
  });
});
