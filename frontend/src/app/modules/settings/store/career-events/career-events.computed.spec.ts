import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus, ERROR_MESSAGES} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {type CareerEvent} from '../../models/career-event/career-event.model';
import {careerEventsComputed} from './career-events.computed';
import {FUTURE_LEAVE, FUTURE_RAISE, PAST_BONUS, RETIRED_COST} from './career-events.fixtures';

function build(
  overrides: Partial<{
    events: CareerEvent[];
    eventsStatus: AsyncStatus;
    actionErrorCode: Nullable<string>;
  }> = {}
) {
  return {
    events: signal<CareerEvent[]>(overrides.events ?? []),
    eventsStatus: signal<AsyncStatus>(overrides.eventsStatus ?? 'idle'),
    actionErrorCode: signal<Nullable<string>>(overrides.actionErrorCode ?? null),
  };
}

function compute(store: ReturnType<typeof build>) {
  return TestBed.runInInjectionContext(() => careerEventsComputed(store));
}

describe('careerEventsComputed', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY}],
    });
  });

  it('is loading only while nothing is on screen', () => {
    expect(compute(build({eventsStatus: 'loading'})).isLoading()).toBe(true);
    expect(compute(build({eventsStatus: 'loading', events: [FUTURE_RAISE]})).isLoading()).toBe(
      false
    );
  });

  it('describes an income change by its kind and date, and keeps its own sign', () => {
    const [row] = compute(build({events: [FUTURE_RAISE]})).rows();

    expect(row.sublabel).toBe('Income change · Mon 1 Mar 2027');
    expect(row.signedAmount).toBe(500);
    expect(row.monthly).toBe(true);
  });

  it('shows a leave as a period, with the amount as a monthly cost', () => {
    const [row] = compute(build({events: [FUTURE_LEAVE]})).rows();

    expect(row.sublabel).toBe('Leave · Tue 1 Jun 2027 to Tue 31 Aug 2027');
    expect(row.signedAmount).toBe(-1200);
    expect(row.monthly).toBe(true);
  });

  it('shows a one-off in as positive and once, and a one-off cost as negative', () => {
    const rows = compute(build({events: [PAST_BONUS, RETIRED_COST]})).rows();

    expect(rows[0].signedAmount).toBe(2000);
    expect(rows[0].monthly).toBe(false);
    expect(rows[1].signedAmount).toBe(-900);
  });

  it('says why an event no longer counts', () => {
    const rows = compute(build({events: [PAST_BONUS, RETIRED_COST]})).rows();

    expect(rows[0].sublabel).toContain('already happened');
    expect(rows[1].sublabel).toContain('retired');
    expect(rows[1].sublabel).not.toContain('already happened');
  });

  it('has no action error message without a code', () => {
    expect(compute(build()).actionErrorMessage()).toBe('');
  });

  it('resolves a registered code to its message', () => {
    const {actionErrorMessage} = compute(build({actionErrorCode: 'CAREER_EVENT_NOT_FOUND'}));

    expect(actionErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['CAREER_EVENT_NOT_FOUND']);
  });

  it('falls back for an unregistered code', () => {
    const {actionErrorMessage} = compute(build({actionErrorCode: 'SOMETHING_ELSE'}));

    expect(actionErrorMessage()).toBe('Could not update the career event.');
  });
});
