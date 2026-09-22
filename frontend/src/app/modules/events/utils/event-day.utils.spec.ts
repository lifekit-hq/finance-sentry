import {describe, expect, it} from 'vitest';

import {type UpcomingEvent} from '../models/event/event.model';
import {EventDayUtils} from './event-day.utils';

function event(date: string, subject = 'MU'): UpcomingEvent {
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

describe('EventDayUtils.toIsoDate', () => {
  it('formats the local calendar date', () => {
    expect(EventDayUtils.toIsoDate(new Date(2026, 8, 22, 23, 30))).toBe('2026-09-22');
  });
});

describe('EventDayUtils.addDays', () => {
  it('adds whole days across a month boundary', () => {
    expect(EventDayUtils.addDays('2026-09-22', 30)).toBe('2026-10-22');
  });

  it('subtracts when negative', () => {
    expect(EventDayUtils.addDays('2026-03-01', -1)).toBe('2026-02-28');
  });
});

describe('EventDayUtils.daysUntil', () => {
  it('is zero for today, positive ahead, negative behind', () => {
    expect(EventDayUtils.daysUntil('2026-09-22', '2026-09-22')).toBe(0);
    expect(EventDayUtils.daysUntil('2026-09-25', '2026-09-22')).toBe(3);
    expect(EventDayUtils.daysUntil('2026-09-20', '2026-09-22')).toBe(-2);
  });
});

describe('EventDayUtils.dayLabel', () => {
  it('names today and tomorrow', () => {
    expect(EventDayUtils.dayLabel('2026-09-22', '2026-09-22')).toBe('Today');
    expect(EventDayUtils.dayLabel('2026-09-23', '2026-09-22')).toBe('Tomorrow');
  });

  it('formats any other day as weekday, day and month', () => {
    expect(EventDayUtils.dayLabel('2026-10-05', '2026-09-22')).toBe('Mon 5 Oct');
  });

  it('formats a past day the same way', () => {
    expect(EventDayUtils.dayLabel('2026-09-18', '2026-09-22')).toBe('Fri 18 Sept');
  });
});

describe('EventDayUtils.groupByDay', () => {
  it('returns no groups for no events', () => {
    expect(EventDayUtils.groupByDay([])).toEqual([]);
  });

  it('groups by date in ascending order and keeps item order within a day', () => {
    const groups = EventDayUtils.groupByDay([
      event('2026-10-01', 'PLTR'),
      event('2026-09-24', 'MU'),
      event('2026-10-01', 'AAPL'),
    ]);

    expect(groups.map(g => g.date)).toEqual(['2026-09-24', '2026-10-01']);
    expect(groups[1].items.map(i => i.subject)).toEqual(['PLTR', 'AAPL']);
  });
});
