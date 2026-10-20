import {DatePipe} from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  CUSTOM_ELEMENTS_SCHEMA,
  inject,
} from '@angular/core';
import {RouterLink} from '@angular/router';
import {type ChipRowChangeDetail, type ChipRowOption} from '@lifekit-hq/elements';
import {
  AlertComponent,
  AsyncStateComponent,
  type AsyncStateStatus,
  CmnTab,
  EmptyStateComponent,
  type Nullable,
  PageContainerComponent,
  SkeletonComponent,
  TabGroupComponent,
  TagComponent,
} from '@lifekit-hq/ui';

import {AlertNavigation} from '../../../alerts/models/alert/alert-destination.model';
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
  type FiredEvent,
  type FiredEventKind,
  type UpcomingEvent,
} from '../../models/event/event.model';
import {EventDayLabelPipe} from '../../pipes/event-day-label.pipe';
import {EventTitlePipe} from '../../pipes/event-title.pipe';
import {EventsStore} from '../../store/events.store';
import {EventDestinationUtils} from '../../utils/event-destination.utils';

const SKELETON_ROWS = 4;

@Component({
  selector: 'fns-events',
  imports: [
    PageContainerComponent,
    AlertComponent,
    AsyncStateComponent,
    DatePipe,
    EmptyStateComponent,
    EventDayLabelPipe,
    EventTitlePipe,
    RouterLink,
    SkeletonComponent,
    TabGroupComponent,
    TagComponent,
  ],
  providers: [EventsStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  host: {class: 'block md:h-full'},
  templateUrl: './events.component.html',
})
export class EventsComponent {
  public readonly store = inject(EventsStore);
  public readonly viewTabs: CmnTab[] = [...EVENTS_VIEW_OPTIONS];
  // lk-chip-row carries string values; the handlers convert back.
  public readonly horizonOptions: ChipRowOption[] = EVENT_HORIZON_DAYS.map(days => ({
    value: String(days),
    label: `${days} days`,
  }));
  public readonly kindOptions: ChipRowOption[] = EVENT_KIND_ORDER.map(kind => ({
    value: kind,
    label: EVENT_KIND_META_REGISTRY[kind].label,
  }));
  public readonly selectedHorizon = computed(() => [String(this.store.horizonDays())]);
  public readonly skeletonRows = Array.from({length: SKELETON_ROWS}, (_, i) => i);
  public readonly upcomingStatus = computed<AsyncStateStatus>(() => {
    if (this.store.upcomingErrorMessage()) {
      return 'error';
    }
    return this.store.isUpcomingLoading() ? 'loading' : 'success';
  });

  // Rows already on screen stay under the error banner; only a first page with nothing yet swaps
  // for the skeleton. Appending a page is the infinite list's state (skeleton rows, inline retry).
  public readonly firedStatus = computed<AsyncStateStatus>(() => {
    if (this.store.firedErrorMessage()) {
      return 'error';
    }
    return this.store.isFiredLoading() && !this.store.hasFiredRows() ? 'loading' : 'success';
  });

  public selectView(id: string): void {
    const option = EVENTS_VIEW_OPTIONS.find(opt => opt.id === id);
    if (option) {
      this.store.setView(option.id);
    }
  }

  public onHorizonChange(event: Event): void {
    const {value} = (event as CustomEvent<ChipRowChangeDetail>).detail;
    this.store.selectHorizon(Number(value));
  }

  // An empty selection means every kind, so the chips light up only for an explicit pick.
  public onKindChange(event: Event): void {
    const {value} = (event as CustomEvent<ChipRowChangeDetail>).detail;
    this.store.toggleKind(value as EventKind);
  }

  public retryFired(): void {
    this.store.loadFired();
  }

  public kindMeta(kind: EventKind): EventKindMeta {
    return EVENT_KIND_META_REGISTRY[kind];
  }

  public firedKindMeta(kind: FiredEventKind): EventKindMeta {
    return FIRED_KIND_META_REGISTRY[kind];
  }

  public outcomeMeta(outcome: EventOutcome): OutcomeMeta {
    return OUTCOME_META_REGISTRY[outcome];
  }

  public upcomingTarget(event: UpcomingEvent): Nullable<{commands: string[]}> {
    const target = EventDestinationUtils.upcoming(event);
    return target?.kind === 'route' ? target : null;
  }

  public firedTarget(event: FiredEvent): Nullable<AlertNavigation> {
    return EventDestinationUtils.fired(event);
  }

  public routeCommands(target: Nullable<AlertNavigation>): string[] {
    return target?.kind === 'route' ? target.commands : [];
  }

  public externalUrl(target: Nullable<AlertNavigation>): string {
    return target?.kind === 'external' ? target.url : '';
  }
}
