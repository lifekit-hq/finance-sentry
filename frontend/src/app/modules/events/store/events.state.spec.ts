import {describe, expect, it} from 'vitest';

import {initialEventsState} from './events.state';

describe('initialEventsState', () => {
  it('opens on the calendar with a 30-day horizon and every kind', () => {
    expect(initialEventsState.view).toBe('calendar');
    expect(initialEventsState.horizonDays).toBe(30);
    expect(initialEventsState.kinds).toEqual([]);
    expect(initialEventsState.upcomingStatus).toBe('idle');
    expect(initialEventsState.firedStatus).toBe('idle');
    expect(initialEventsState.firedPage).toBe(1);
  });
});
