import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../core/errors/error-messages.registry';
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
    upcomingErrorCode: Nullable<string>;
    fired: FiredEvent[];
    firedTotalCount: number;
    firedStatus: AsyncStatus;
    firedErrorCode: Nullable<string>;
  }> = {}
) {
  return {
    horizonDays: signal(overrides.horizonDays ?? 30),
    kinds: signal<EventKind[]>(overrides.kinds ?? []),
    upcoming: signal<UpcomingEvent[]>(overrides.upcoming ?? []),
    sources: signal<EventSourceStatusEntry[]>(overrides.sources ?? []),
    upcomingStatus: signal<AsyncStatus>(overrides.upcomingStatus ?? 'idle'),
    upcomingErrorCode: signal<Nullable<string>>(overrides.upcomingErrorCode ?? null),
    fired: signal<FiredEvent[]>(overrides.fired ?? []),
    firedTotalCount: signal(overrides.firedTotalCount ?? 0),
    firedStatus: signal<AsyncStatus>(overrides.firedStatus ?? 'idle'),
    firedErrorCode: signal<Nullable<string>>(overrides.firedErrorCode ?? null),
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
    TestBed.configureTestingModule({
      providers: [{provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY}],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('calendarWindow runs from today to today plus the horizon', () => {
    const computed = build({horizonDays: 90});

    expect(computed.calendarWindow()).toEqual({from: '2026-09-22', to: '2026-12-21'});
  });

  it('upcomingByDay groups items by date', () => {
    const computed = build({
      upcoming: [upcoming('2026-10-01', 'PLTR'), upcoming('2026-09-22', 'MU')],
    });

    expect(computed.upcomingByDay().map(g => g.date)).toEqual(['2026-09-22', '2026-10-01']);
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

  it('isKindSelected treats an empty selection as every kind', () => {
    expect(build().isKindSelected()('macro')).toBe(true);
    const narrowed = build({kinds: ['earnings']});
    expect(narrowed.isKindSelected()('earnings')).toBe(true);
    expect(narrowed.isKindSelected()('macro')).toBe(false);
  });

  it('isUpcomingEmpty is true only when idle with nothing loaded', () => {
    expect(build().isUpcomingEmpty()).toBe(true);
    expect(build({upcomingStatus: 'loading'}).isUpcomingEmpty()).toBe(false);
    expect(build({upcoming: [upcoming('2026-09-22', 'MU')]}).isUpcomingEmpty()).toBe(false);
  });

  it('upcomingErrorMessage resolves a known code and falls back otherwise', () => {
    expect(
      build({
        upcomingStatus: 'error',
        upcomingErrorCode: 'EVENTS_WINDOW_INVALID',
      }).upcomingErrorMessage()
    ).toContain('window is invalid');
    expect(build({upcomingStatus: 'error', upcomingErrorCode: 'NOPE'}).upcomingErrorMessage()).toBe(
      'Failed to load upcoming events.'
    );
    expect(build().upcomingErrorMessage()).toBe('');
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

  it('hasMoreFired compares the loaded count with the total', () => {
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
    expect(build({fired: [one], firedTotalCount: 2}).hasMoreFired()).toBe(true);
    expect(build({fired: [one], firedTotalCount: 1}).hasMoreFired()).toBe(false);
  });

  it('firedErrorMessage falls back to the feed default', () => {
    expect(build({firedStatus: 'error', firedErrorCode: 'NOPE'}).firedErrorMessage()).toBe(
      'Failed to load fired events.'
    );
  });
});
