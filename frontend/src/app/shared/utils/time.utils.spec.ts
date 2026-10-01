import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {TimeUtils} from './time.utils';

const NOW = new Date('2026-04-25T12:00:00Z');

describe('TimeUtils.getRelativeTime', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns "Never" for null or empty input', () => {
    expect(TimeUtils.getRelativeTime(null)).toBe('Never');
    expect(TimeUtils.getRelativeTime('')).toBe('Never');
  });

  it('returns "Just now" when less than a minute has passed', () => {
    const fortySecondsAgo = new Date(NOW.getTime() - 40 * 1000).toISOString();
    expect(TimeUtils.getRelativeTime(fortySecondsAgo)).toBe('Just now');
  });

  it('returns minutes-ago for under an hour', () => {
    const fifteenMinutesAgo = new Date(NOW.getTime() - 15 * 60 * 1000).toISOString();
    expect(TimeUtils.getRelativeTime(fifteenMinutesAgo)).toBe('15m ago');
  });

  it('returns hours-ago for an hour or more', () => {
    const threeHoursAgo = new Date(NOW.getTime() - 3 * 60 * 60 * 1000).toISOString();
    expect(TimeUtils.getRelativeTime(threeHoursAgo)).toBe('3h ago');
  });

  it('returns days-ago from 24 hours on', () => {
    const fiveDaysAgo = new Date(NOW.getTime() - 5 * 24 * 60 * 60 * 1000).toISOString();
    expect(TimeUtils.getRelativeTime(fiveDaysAgo)).toBe('5d ago');
  });

  it('keeps hours just under a day', () => {
    const twentyThreeHoursAgo = new Date(NOW.getTime() - 23 * 60 * 60 * 1000).toISOString();
    expect(TimeUtils.getRelativeTime(twentyThreeHoursAgo)).toBe('23h ago');
  });

  it('rounds down minutes', () => {
    const ninetyEightSecondsAgo = new Date(NOW.getTime() - 98 * 1000).toISOString();
    expect(TimeUtils.getRelativeTime(ninetyEightSecondsAgo)).toBe('1m ago');
  });
});
