import {type Nullable} from '@lifekit-hq/ui';

import {type CareerEvent} from '../../models/career-event/career-event.model';

export interface CareerEventsState {
  events: CareerEvent[];
  /** The event a retire or delete is in flight for. */
  busyId: Nullable<string>;
  actionErrorCode: Nullable<string>;
}

export const initialCareerEventsState: CareerEventsState = {
  events: [],
  busyId: null,
  actionErrorCode: null,
};
