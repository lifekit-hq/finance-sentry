import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type CalendarWindow,
  type EventDayGroup,
  type EventKind,
  type EventsView,
  type FiredEvent,
  type UpcomingEvent,
} from '../../models/event/event.model';
import {EventsStore} from '../../store/events.store';
import {EventsComponent} from './events.component';

const NOW = new Date('2026-09-22T12:00:00Z');

function upcoming(
  date: string,
  subject: string,
  overrides: Partial<UpcomingEvent> = {}
): UpcomingEvent {
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
    ...overrides,
  };
}

function fired(
  alertId: string,
  outcome: FiredEvent['outcome'],
  verdict: FiredEvent['verdict'] = null
): FiredEvent {
  return {
    alertId,
    kind: 'NewsCluster',
    severity: 'Warning',
    subject: 'MU',
    title: `News cluster: ${alertId}`,
    message: 'clustered',
    occurredAt: '2026-09-22T10:00:00Z',
    isRead: false,
    delivery: null,
    verdict,
    outcome,
  };
}

function groupsOf(items: UpcomingEvent[]): EventDayGroup[] {
  const map = new Map<string, UpcomingEvent[]>();
  for (const item of items) {
    map.set(item.date, [...(map.get(item.date) ?? []), item]);
  }
  return [...map.entries()].map(([date, dayItems]) => ({date, items: dayItems}));
}

describe('EventsComponent', () => {
  let mockStore: {
    view: ReturnType<typeof signal<EventsView>>;
    horizonDays: ReturnType<typeof signal<number>>;
    isKindSelected: ReturnType<typeof signal<(kind: EventKind) => boolean>>;
    unavailableSourceLabels: ReturnType<typeof signal<string[]>>;
    upcomingErrorMessage: ReturnType<typeof signal<string>>;
    isUpcomingLoading: ReturnType<typeof signal<boolean>>;
    isUpcomingEmpty: ReturnType<typeof signal<boolean>>;
    upcomingByDay: ReturnType<typeof signal<EventDayGroup[]>>;
    calendarWindow: ReturnType<typeof signal<CalendarWindow>>;
    firedErrorMessage: ReturnType<typeof signal<string>>;
    isFiredEmpty: ReturnType<typeof signal<boolean>>;
    isFiredLoading: ReturnType<typeof signal<boolean>>;
    hasMoreFired: ReturnType<typeof signal<boolean>>;
    hasFiredRows: ReturnType<typeof signal<boolean>>;
    fired: ReturnType<typeof signal<FiredEvent[]>>;
    setView: ReturnType<typeof vi.fn>;
    selectHorizon: ReturnType<typeof vi.fn>;
    toggleKind: ReturnType<typeof vi.fn>;
    loadMoreFired: ReturnType<typeof vi.fn>;
  };

  beforeEach(async () => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    const items = [
      upcoming('2026-09-22', 'MU', {isEstimate: true}),
      upcoming('2026-09-23', 'PLTR', {kind: 'ex_dividend', title: 'Ex-dividend: PLTR'}),
      upcoming('2026-10-05', 'US', {
        kind: 'macro',
        title: 'CPI (Sep)',
        time: '08:30:00',
        detail: 'US · high importance',
      }),
    ];
    mockStore = {
      view: signal<EventsView>('calendar'),
      horizonDays: signal(30),
      isKindSelected: signal(() => true),
      unavailableSourceLabels: signal<string[]>([]),
      upcomingErrorMessage: signal(''),
      isUpcomingLoading: signal(false),
      isUpcomingEmpty: signal(false),
      upcomingByDay: signal(groupsOf(items)),
      calendarWindow: signal({from: '2026-09-22', to: '2026-10-22'}),
      firedErrorMessage: signal(''),
      isFiredEmpty: signal(false),
      isFiredLoading: signal(false),
      hasMoreFired: signal(false),
      hasFiredRows: signal(true),
      fired: signal<FiredEvent[]>([
        fired('a', 'silent'),
        fired('b', 'judged_immaterial', {
          text: 'Guidance cut is priced in.',
          notified: false,
          recordedAt: '2026-09-22T11:00:00Z',
        }),
        fired('c', 'not_delivered'),
      ]),
      setView: vi.fn(),
      selectHorizon: vi.fn(),
      toggleKind: vi.fn(),
      loadMoreFired: vi.fn(),
    };

    await TestBed.configureTestingModule({imports: [EventsComponent]})
      .overrideComponent(EventsComponent, {
        set: {providers: [{provide: EventsStore, useValue: mockStore}]},
      })
      .compileComponents();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function render() {
    const fixture = TestBed.createComponent(EventsComponent);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    return {fixture, host, text: host.textContent ?? ''};
  }

  it('groups the calendar by day with Today and Tomorrow headers', () => {
    const {host, text} = render();

    expect(text).toContain('Today');
    expect(text).toContain('Tomorrow');
    expect(text).toContain('Mon 5 Oct');
    expect(host.querySelectorAll('[data-testid^="day-"]').length).toBe(3);
  });

  it('renders kind, subject, estimate marker, detail and time on the rows', () => {
    const {text} = render();

    expect(text).toContain('Earnings');
    expect(text).toContain('MU');
    expect(text).toContain('Estimate');
    expect(text).toContain('Ex-dividend: PLTR');
    expect(text).toContain('US · high importance');
    expect(text).toContain('08:30');
  });

  it('shows which sources did not answer without hiding the rest', () => {
    mockStore.unavailableSourceLabels.set(['earnings and ex-dividend dates']);

    const {host, text} = render();

    expect(host.querySelector('[data-testid="sources-unavailable"]')).not.toBeNull();
    expect(text).toContain('earnings and ex-dividend dates');
    expect(text).toContain('Today');
  });

  it('shows the calendar empty state when nothing is scheduled', () => {
    mockStore.upcomingByDay.set([]);
    mockStore.isUpcomingEmpty.set(true);

    expect(render().text).toContain('Nothing scheduled');
  });

  it('dispatches horizon changes to the store', () => {
    const {host} = render();
    const chips = host.querySelectorAll('[data-testid="horizon-chip"]');

    expect(chips.length).toBe(3);
    (chips[2] as HTMLElement).querySelector('button')?.click();

    expect(mockStore.selectHorizon).toHaveBeenCalledWith(90);
  });

  it('renders the fired feed with an outcome label per event and the verdict text', () => {
    mockStore.view.set('fired');

    const {host, text} = render();

    const outcomes = Array.from(host.querySelectorAll('[data-testid="outcome"]')).map(
      el => el.textContent?.trim() ?? ''
    );
    expect(outcomes).toEqual(['Silent', 'Judged immaterial', 'Not delivered']);
    expect(text).toContain('Guidance cut is priced in.');
    expect(text).toContain('Kept quiet');
    expect(host.querySelectorAll('[data-testid="verdict"]').length).toBe(1);
    expect(text).toContain('silence is the answer');
  });

  it('shows the fired empty state when nothing has fired', () => {
    mockStore.view.set('fired');
    mockStore.fired.set([]);
    mockStore.hasFiredRows.set(false);
    mockStore.isFiredEmpty.set(true);

    expect(render().text).toContain('Nothing has fired');
  });

  it('keeps the loaded rows on screen when the next page fails', () => {
    mockStore.view.set('fired');
    mockStore.hasMoreFired.set(true);
    mockStore.firedErrorMessage.set('Failed to load fired events.');

    const {host, text} = render();

    expect(host.querySelectorAll('[data-testid="fired-event"]').length).toBe(3);
    expect(host.querySelector('[data-testid="fired-more-error"]')).not.toBeNull();
    expect(text).toContain('Failed to load fired events.');
    expect(text).toContain('Load more');
  });

  it('shows only the error when the first page fails', () => {
    mockStore.view.set('fired');
    mockStore.fired.set([]);
    mockStore.hasFiredRows.set(false);
    mockStore.firedErrorMessage.set('Failed to load fired events.');

    const {host, text} = render();

    expect(host.querySelectorAll('[data-testid="fired-event"]').length).toBe(0);
    expect(host.querySelector('[data-testid="fired-more-error"]')).toBeNull();
    expect(text).toContain('Failed to load fired events.');
  });

  it('offers load more only while the feed has more rows', () => {
    mockStore.view.set('fired');

    expect(render().text).not.toContain('Load more');

    mockStore.hasMoreFired.set(true);
    expect(render().text).toContain('Load more');
  });
});
