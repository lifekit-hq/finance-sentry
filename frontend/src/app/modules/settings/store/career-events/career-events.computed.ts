import {computed, inject, type Signal} from '@angular/core';
import {type AsyncStatus, ErrorMessageService} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

import {DateUtils} from '../../../../shared/utils/date.utils';
import {CAREER_EVENT_KIND_SHORT_LABEL} from '../../constants/career-event/career-event.constants';
import {type CareerEvent, type CareerEventRow} from '../../models/career-event/career-event.model';

interface StateSignals {
  events: Signal<CareerEvent[]>;
  eventsStatus: Signal<AsyncStatus>;
  actionErrorCode: Signal<Nullable<string>>;
}

/** The amount with the sign it has on the saving: only an income step carries its own sign. */
function signedAmount(event: CareerEvent): number {
  return event.kind === 'OneOffOut' || event.kind === 'Leave' ? -event.amount : event.amount;
}

function sublabel(event: CareerEvent): string {
  const when = event.endDate
    ? `${DateUtils.dayLabel(event.date, true)} to ${DateUtils.dayLabel(event.endDate, true)}`
    : DateUtils.dayLabel(event.date, true);
  const parts = [CAREER_EVENT_KIND_SHORT_LABEL.get(event.kind) ?? event.kind, when];
  if (event.retired) {
    parts.push('retired');
  } else if (!event.counted) {
    parts.push('already happened');
  }
  return parts.join(' · ');
}

export function careerEventsComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    isLoading: computed(() => store.eventsStatus() === 'loading' && store.events().length === 0),
    rows: computed<CareerEventRow[]>(() =>
      store.events().map(event => ({
        ...event,
        sublabel: sublabel(event),
        signedAmount: signedAmount(event),
        monthly: event.kind === 'IncomeStep' || event.kind === 'Leave',
      }))
    ),
    actionErrorMessage: computed(() => {
      const code = store.actionErrorCode();
      return code ? (errorMessages.resolve(code) ?? 'Could not update the career event.') : '';
    }),
  };
}
