import {DatePipe} from '@angular/common';
import {ChangeDetectionStrategy, Component, inject} from '@angular/core';
import {
  AlertComponent,
  ButtonComponent,
  ChipComponent,
  EmptyStateComponent,
  PageHeaderComponent,
  SkeletonComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {
  EVENT_HORIZON_DAYS,
  EVENT_KIND_META_REGISTRY,
  EVENT_KIND_ORDER,
  type EventKindMeta,
  EVENTS_VIEW_OPTIONS,
  FIRED_KIND_META_REGISTRY,
  OUTCOME_META_REGISTRY,
  type OutcomeMeta,
} from '../../constants/event/event.constants';
import {
  type EventKind,
  type EventOutcome,
  type FiredEventKind,
} from '../../models/event/event.model';
import {EventDayLabelPipe} from '../../pipes/event-day-label.pipe';
import {EventsStore} from '../../store/events.store';

const SKELETON_ROWS = 4;

@Component({
  selector: 'fns-events',
  imports: [
    AlertComponent,
    ButtonComponent,
    ChipComponent,
    DatePipe,
    EmptyStateComponent,
    EventDayLabelPipe,
    PageHeaderComponent,
    SkeletonComponent,
    TagComponent,
  ],
  providers: [EventsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {class: 'block h-full'},
  templateUrl: './events.component.html',
})
export class EventsComponent {
  public readonly store = inject(EventsStore);
  public readonly viewOptions = EVENTS_VIEW_OPTIONS;
  public readonly horizons = EVENT_HORIZON_DAYS;
  public readonly kinds = EVENT_KIND_ORDER;
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS}, (_, i) => i);

  public kindMeta(kind: EventKind): EventKindMeta {
    return EVENT_KIND_META_REGISTRY[kind];
  }

  public firedKindMeta(kind: FiredEventKind): EventKindMeta {
    return FIRED_KIND_META_REGISTRY[kind];
  }

  public outcomeMeta(outcome: EventOutcome): OutcomeMeta {
    return OUTCOME_META_REGISTRY[outcome];
  }
}
