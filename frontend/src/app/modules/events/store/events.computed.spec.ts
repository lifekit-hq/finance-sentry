import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type EventKind,
  type EventSourceStatusEntry,
  type FiredEvent,
  type UpcomingEvent,
} from '../models/event/event.model';
import {eventsComputed} from './events.computed';

const NOW = new Date('2026-09-22T12:00:00Z');

function upcoming(date: string, subject: string): UpcomingEvent {
  return {
    kind: 'earnings',
    date,
    time: null,
    subject,
    title: `Earnings: ${subject}`,
    detail: null,
    isEstimate: false,
    source: 'yahoo',
    referenceId: null,
  };
}

function buildSignals(
  overrides: Partial<{
    horizonDays: number;
    kinds: EventKind[];
    upcoming: UpcomingEvent[];
    sources: EventSourceStatusEntry[];
    upcomingStatus: AsyncStatus;
    fired: FiredEvent[];
    firedTotalCount: number;
    firedPage: number;
    firedPageSize: number;
    firedStatus: AsyncStatus;
  }> = {}
) {
  return {
    horizonDays: signal(overrides.horizonDays ?? 30),
    kinds: signal<EventKind[]>(overrides.kinds ?? []),
    upcoming: signal<UpcomingEvent[]>(overrides.upcoming ?? []),
    sources: signal<EventSourceStatusEntry[]>(overrides.sources ?? []),
    upcomingStatus: signal<AsyncStatus>(overrides.upcomingStatus ?? 'success'),
    fired: signal<FiredEvent[]>(overrides.fired ?? []),
    firedTotalCount: signal(overrides.firedTotalCount ?? 0),
    firedPage: signal(overrides.firedPage ?? 1),
    firedPageSize: signal(overrides.firedPageSize ?? 20),
    firedStatus: signal<AsyncStatus>(overrides.firedStatus ?? 'success'),
  };
}

function build(overrides: Parameters<typeof buildSignals>[0] = {}) {
  return TestBed.runInInjectionContext(() => eventsComputed(buildSignals(overrides)));
}

describe('eventsComputed', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('calendarWindow runs from today to today plus the horizon', () => {
    const computed = build({horizonDays: 90});

    expect(computed.calendarWindow()).toEqual({from: '2026-09-22', to: '2026-12-21'});
  });

  it('unavailableSourceLabels names only the failed sources in words', () => {
    const computed = build({
      sources: [
        {source: 'corporate', status: 'unavailable'},
        {source: 'macro', status: 'ok'},
        {source: 'filings', status: 'unavailable'},
      ],
    });

    expect(computed.unavailableSourceLabels()).toEqual([
      'earnings and ex-dividend dates',
      'filing due dates',
    ]);
  });

  it('upcomingSections splits days into this week and later, dropping empty sections', () => {
    const both = build({
      upcoming: [upcoming('2026-09-23', 'MU'), upcoming('2026-10-20', 'PLTR')],
    });
    expect(both.upcomingSections().map(s => [s.id, s.groups.length])).toEqual([
      ['week', 1],
      ['later', 1],
    ]);

    const onlyLater = build({upcoming: [upcoming('2026-10-20', 'PLTR')]});
    expect(onlyLater.upcomingSections().map(s => s.id)).toEqual(['later']);
    expect(build().upcomingSections()).toEqual([]);
  });

  it('isUpcomingEmpty is true only once loaded with nothing to show', () => {
    expect(build().isUpcomingEmpty()).toBe(true);
    expect(build({upcomingStatus: 'loading'}).isUpcomingEmpty()).toBe(false);
    expect(build({upcoming: [upcoming('2026-09-22', 'MU')]}).isUpcomingEmpty()).toBe(false);
  });

  it('isFiredEmpty is true only once loaded with nothing to show', () => {
    expect(build().isFiredEmpty()).toBe(true);
    expect(build({firedStatus: 'loading'}).isFiredEmpty()).toBe(false);
    expect(build({firedStatus: 'idle'}).isFiredEmpty()).toBe(false);
  });

  it('hasFiredRows is true once any row is loaded', () => {
    const one: FiredEvent = {
      alertId: 'a',
      kind: 'NewsCluster',
      severity: 'Warning',
      subject: 'MU',
      title: 't',
      message: 'm',
      occurredAt: '2026-09-22T10:00:00Z',
      isRead: false,
      delivery: null,
      verdict: null,
      outcome: 'awaiting',
    };
    expect(build().hasFiredRows()).toBe(false);
    expect(build({fired: [one], firedStatus: 'error'}).hasFiredRows()).toBe(true);
  });

  it('hasMoreFired is decided by the page cursor, not by how many rows survived dedup', () => {
    expect(build({firedPage: 1, firedPageSize: 20, firedTotalCount: 25}).hasMoreFired()).toBe(true);
    expect(build({firedPage: 2, firedPageSize: 20, firedTotalCount: 25}).hasMoreFired()).toBe(
      false
    );
    expect(build({firedPage: 1, firedPageSize: 20, firedTotalCount: 20}).hasMoreFired()).toBe(
      false
    );
    expect(
      build({fired: [], firedPage: 2, firedPageSize: 20, firedTotalCount: 25}).hasMoreFired()
    ).toBe(false);
  });
});
