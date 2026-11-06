import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {CmnDialogService} from '@lifekit-hq/ui';
import {of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {
  type CareerEventInput,
  type CareerEventRow,
} from '../../models/career-event/career-event.model';
import {
  FUTURE_LEAVE,
  FUTURE_RAISE,
  PAST_BONUS,
} from '../../store/career-events/career-events.fixtures';
import {CareerEventsStore} from '../../store/career-events/career-events.store';
import {CareerTimelineComponent} from './career-timeline.component';

const INPUT: CareerEventInput = {
  kind: 'IncomeStep',
  date: '2027-03-01',
  endDate: null,
  label: 'Promotion',
  amount: 500,
  currency: 'EUR',
  note: null,
};

const ROWS: CareerEventRow[] = [
  {
    ...PAST_BONUS,
    sublabel: 'One-off in · Thu 15 Jan 2026 · already happened, part of your baseline',
    signedAmount: 2000,
    monthly: false,
  },
  {...FUTURE_RAISE, sublabel: 'Income change · Mon 1 Mar 2027', signedAmount: 500, monthly: true},
  {
    ...FUTURE_LEAVE,
    sublabel: 'Leave · Tue 1 Jun 2027 to Tue 31 Aug 2027',
    signedAmount: -1200,
    monthly: true,
  },
];

function setup(dialogResult?: unknown, rows: CareerEventRow[] = ROWS) {
  const store = {
    rows: signal<CareerEventRow[]>(rows),
    isLoading: signal(false),
    eventsErrorMessage: signal(''),
    saveErrorMessage: signal(''),
    actionErrorMessage: signal(''),
    isSaveLoading: signal(false),
    load: vi.fn(),
    save: vi.fn(),
    retire: vi.fn(),
    remove: vi.fn(),
  };
  const dialog = {open: vi.fn(() => ({afterClosed: () => of(dialogResult)}))};
  TestBed.configureTestingModule({
    providers: [
      {provide: CmnDialogService, useValue: dialog},
      {provide: ConnectivityService, useValue: {offline: signal(false)}},
    ],
  });
  TestBed.overrideComponent(CareerTimelineComponent, {
    set: {providers: [{provide: CareerEventsStore, useValue: store}]},
  });
  const fixture = TestBed.createComponent(CareerTimelineComponent);
  fixture.detectChanges();
  return {store, dialog, fixture, component: fixture.componentInstance};
}

describe('CareerTimelineComponent', () => {
  beforeEach(() => TestBed.resetTestingModule());

  it('says it is a scenario the person entered, not a forecast', () => {
    const {fixture} = setup();

    const text = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="career-timeline-disclosure"]'
    )?.textContent;
    expect(text).toContain('A scenario you entered, not a forecast.');
    expect(text).toContain('part of your baseline');
  });

  it('lists each event with its signed amount, per month where it repeats', () => {
    const {fixture} = setup();
    const el = fixture.nativeElement as HTMLElement;

    const rows = el.querySelectorAll('[data-testid="career-event-row"]');
    expect(rows).toHaveLength(3);
    expect(rows[1].textContent).toContain('Promotion');
    expect(rows[1].querySelector('[data-testid="career-event-amount"]')?.textContent).toContain(
      '€500.00'
    );
    expect(rows[1].textContent).toContain('per month');
    expect(rows[0].textContent).not.toContain('per month');
    expect(rows[2].querySelector('[data-testid="career-event-amount"]')?.textContent).toContain(
      '1,200.00'
    );
  });

  it('shows the empty state with no events', () => {
    const {fixture} = setup(undefined, []);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No career events yet');
  });

  it('shows the save error above the list', () => {
    const {store, fixture} = setup();
    store.saveErrorMessage.set('Could not save the career event.');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Could not save the career event.'
    );
  });

  it('saves a new event from the add dialog', () => {
    const {store, component} = setup(INPUT);

    component.openEvent();

    expect(store.save).toHaveBeenCalledWith({id: null, input: INPUT});
  });

  it('saves an edit against the event id', () => {
    const {store, component} = setup(INPUT);

    component.openEvent(ROWS[1]);

    expect(store.save).toHaveBeenCalledWith({id: 'e-1', input: INPUT});
  });

  it('saves nothing when the dialog is dismissed', () => {
    const {store, component} = setup(undefined);

    component.openEvent();

    expect(store.save).not.toHaveBeenCalled();
  });

  it('opens the editor from the menu', () => {
    const {store, component} = setup(INPUT);

    component.onAction(ROWS[1], 'edit');

    expect(store.save).toHaveBeenCalledWith({id: 'e-1', input: INPUT});
  });

  it('retires an event once confirmed', () => {
    const {store, component} = setup(true);

    component.onAction(ROWS[1], 'retire');

    expect(store.retire).toHaveBeenCalledWith('e-1');
  });

  it('does not retire when the confirmation is declined', () => {
    const {store, component} = setup(false);

    component.onAction(ROWS[1], 'retire');

    expect(store.retire).not.toHaveBeenCalled();
  });

  it('deletes an event once confirmed', () => {
    const {store, component} = setup(true);

    component.onAction(ROWS[0], 'delete');

    expect(store.remove).toHaveBeenCalledWith('e-3');
  });

  it('ignores an unknown menu action', () => {
    const {store, dialog, component} = setup(true);

    component.onAction(ROWS[0], 'nope');

    expect(dialog.open).not.toHaveBeenCalled();
    expect(store.retire).not.toHaveBeenCalled();
  });
});
