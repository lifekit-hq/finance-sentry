import {ApplicationRef, getDebugNode, type Signal, signal, type TemplateRef} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {
  ASYNC_STATE_SHOW_DELAY_MS,
  CmnDrawerService,
  type FilterBarSheetContext,
} from '@lifekit-hq/ui';
import {Subject} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {type AlertFilterSheetComponent} from '../../components/alert-filter-sheet/alert-filter-sheet.component';
import {type Alert, type AlertFilter} from '../../models/alert/alert.model';
import {AlertsStore} from '../../store/alerts/alerts.store';
import {AlertsComponent} from './alerts.component';
import {RESOLVED_ALERT_ROW_CLASS} from './alerts.constants';

const UNREAD_COUNT = 3;
const TOTAL_COUNT = 45;

const drawerOpen = vi.fn(() => ({beforeClose$: new Subject<void>(), close: vi.fn()}));

type DismissibleChipElement = HTMLElement & {label: string};

function render(
  filter: AlertFilter = 'all',
  unreadCount = 0,
  alerts: Alert[] = [],
  async: {isLoading?: boolean; errorMessage?: Nullable<string>; search?: string} = {}
) {
  const store = {
    alerts: signal(alerts),
    filter: signal(filter),
    search: signal(async.search ?? ''),
    unreadCount: signal(unreadCount),
    totalCount: signal(TOTAL_COUNT),
    hasMore: signal(true),
    isAppendLoading: signal(false),
    hasAppendError: signal(false),
    loadMore: vi.fn(),
    setFilter: vi.fn(),
    applySearch: vi.fn(),
    isLoading: signal(async.isLoading ?? false),
    errorMessage: signal<Nullable<string>>(async.errorMessage ?? null),
    query: signal({filter, search: ''}),
    load: vi.fn(),
    markRead: vi.fn(),
    markAllRead: vi.fn(),
    dismiss: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {provide: AlertsStore, useValue: store},
      {provide: CmnDrawerService, useValue: {open: drawerOpen}},
    ],
  });
  const fixture = TestBed.createComponent(AlertsComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return {fixture, el, store, component: fixture.componentInstance};
}

function appliedChips(root: HTMLElement): DismissibleChipElement[] {
  return Array.from(root.querySelectorAll<DismissibleChipElement>('lk-dismissible-chip'));
}

function filtersButton(root: HTMLElement): HTMLButtonElement {
  return root.querySelector('cmn-filter-bar cmn-badge button') as HTMLButtonElement;
}

function clearButton(root: HTMLElement): HTMLButtonElement | undefined {
  return Array.from(root.querySelectorAll<HTMLButtonElement>('cmn-filter-bar button')).find(
    b => b.textContent?.trim() === 'Clear'
  );
}

/** Renders the sheet template the filter bar hands the drawer and returns the sheet in it. */
function openSheet(root: HTMLElement, close: () => void): AlertFilterSheetComponent {
  filtersButton(root).click();
  const [, config] = drawerOpen.mock.calls.at(-1) as unknown as [
    unknown,
    {data: {template: TemplateRef<FilterBarSheetContext>}},
  ];
  const view = config.data.template.createEmbeddedView({
    $implicit: drawerOpen.mock.results.at(-1)
      ?.value as unknown as FilterBarSheetContext['$implicit'],
    close,
  });
  TestBed.inject(ApplicationRef).attachView(view);
  view.detectChanges();
  const host = view.rootNodes.find((n: Node) => n instanceof HTMLElement) as HTMLElement;
  return getDebugNode(host)?.componentInstance as AlertFilterSheetComponent;
}

describe('AlertsComponent filter flow', () => {
  beforeEach(() => drawerOpen.mockClear());

  it('shows no applied filter and no badge count for the unfiltered list', () => {
    const {el, component} = render('all', UNREAD_COUNT);

    expect(component.activeFilterCount()).toBe(0);
    expect(appliedChips(el)).toHaveLength(0);
    expect(clearButton(el)).toBeUndefined();
  });

  it('counts the filter and shows it as a removable chip with Clear', () => {
    const {el, component} = render('warning');

    expect(component.activeFilterCount()).toBe(1);
    expect(appliedChips(el).map(c => c.label)).toEqual(['Warnings']);
    expect(clearButton(el)).toBeDefined();
  });

  it('opens the responsive Filters sheet from the filter bar', () => {
    const {el} = render('error');

    filtersButton(el).click();

    expect(drawerOpen).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({title: 'Filters', mode: 'responsive'})
    );
  });

  it('seeds the sheet with the current filter, applies the chosen one and closes', () => {
    const close = vi.fn();
    const {el, store} = render('error');

    const sheet = openSheet(el, close);
    expect(sheet.initial()).toBe('error');
    sheet.applied.emit('unread');

    expect(store.setFilter).toHaveBeenCalledWith('unread');
    expect(close).toHaveBeenCalled();
  });

  it('clears the filter from the chip and from Clear', () => {
    const {el, store} = render('info');

    clearButton(el)?.click();
    appliedChips(el)[0].dispatchEvent(new CustomEvent('lk-dismissible-chip-remove'));

    expect(store.setFilter).toHaveBeenCalledTimes(2);
    expect(store.setFilter).toHaveBeenCalledWith('all');
  });

  it('feeds typed search text to the store', () => {
    const {el, store} = render();
    const feed = store.applySearch.mock.calls[0][0] as Signal<string>;

    const input = el.querySelector('input[type="search"]') as HTMLInputElement;
    input.value = 'rent';
    input.dispatchEvent(new Event('input'));

    expect(feed()).toBe('rent');
  });

  it('starts the search box from the search the store holds', async () => {
    const {el, fixture} = render('all', 0, [], {search: 'rent'});
    await fixture.whenStable();
    fixture.detectChanges();

    expect((el.querySelector('input[type="search"]') as HTMLInputElement).value).toBe('rent');
  });

  it('pages the list through lk-infinite-list instead of a pager', () => {
    const {el, store} = render('all', 0, [
      {
        id: 'a1',
        title: 'T',
        message: 'M',
        severity: 'Info',
        type: 'SyncFailed',
        isRead: true,
        isResolved: false,
        createdAt: '2026-09-22T10:00:00Z',
      } as unknown as Alert,
    ]);
    const list = el.querySelector('[data-testid="alerts-list"]') as HTMLElement & {
      hasMore: boolean;
    };

    expect(el.querySelector('[data-testid="alerts-pager"]')).toBeNull();
    expect(list.hasMore).toBe(true);
    list.dispatchEvent(new CustomEvent('lk-infinite-list-load-more', {bubbles: true}));
    list.dispatchEvent(new CustomEvent('lk-infinite-list-retry', {bubbles: true}));

    expect(store.loadMore).toHaveBeenCalledTimes(2);
  });
});

describe('AlertsComponent async states', () => {
  const ALERT = {
    id: 'a1',
    title: 'Budget exceeded',
    message: 'You went over',
    severity: 'Warning',
    type: 'BudgetExceeded',
    isRead: false,
    isResolved: false,
    createdAt: '2026-09-22T10:00:00Z',
  } as unknown as Alert;

  function retryButton(host: HTMLElement) {
    return Array.from(host.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Retry');
  }

  it('shows the section skeleton, not the empty copy, once a slow load outlasts the delay', async () => {
    const {el, fixture} = render('all', 0, [], {isLoading: true});
    await new Promise(resolve => setTimeout(resolve, ASYNC_STATE_SHOW_DELAY_MS + 50));
    fixture.detectChanges();

    expect(
      el.querySelector('[data-testid="alerts-skeleton"] cmn-skeleton[shape="section"]')
    ).not.toBeNull();
    expect(el.textContent).not.toContain('All your accounts are healthy.');
  });

  it('shows the load error and reloads the current query on Retry', () => {
    const {el, store, fixture} = render('warning', 0, [], {errorMessage: 'Failed to load alerts.'});

    expect(el.textContent).toContain('Failed to load alerts.');
    expect(el.textContent).not.toContain('Try a different filter.');

    retryButton(el)?.click();
    fixture.detectChanges();

    expect(store.load).toHaveBeenCalledWith({filter: 'warning', search: ''});
  });

  it('replaces the previous query rows with the error when the new query fails', () => {
    const {el} = render('error', 1, [ALERT], {errorMessage: 'Failed to load alerts.'});

    expect(el.textContent).toContain('Failed to load alerts.');
    expect(el.querySelector('[data-testid="alert-row"]')).toBeNull();
  });

  it('shows the filter-aware empty state when no alert matches', () => {
    const {el} = render('error');

    expect(el.textContent).toContain('No error alerts');
    expect(el.textContent).toContain('Try a different filter.');
  });

  it('shows the search-aware empty state when the search matches nothing', () => {
    const {el} = render('error', 0, [], {search: 'rent'});

    expect(el.textContent).toContain('No matching alerts');
    expect(el.textContent).toContain('Try a different search or filter.');
  });

  it('shows the all-clear copy when there are no alerts at all', () => {
    const {el} = render('all');

    expect(el.textContent).toContain('No alerts');
    expect(el.textContent).toContain('All your accounts are healthy.');
  });

  it('lists the alerts once loaded', () => {
    const {el} = render('all', 1, [ALERT]);

    expect(el.querySelectorAll('[data-testid="alert-row"]').length).toBe(1);
    expect(el.querySelector('[data-testid="alerts-skeleton"]')).toBeNull();
  });

  it('groups the rows under a labelled day section', () => {
    const older = {...ALERT, id: 'a0', createdAt: '2026-09-20T10:00:00Z'} as unknown as Alert;
    const {el} = render('all', 1, [ALERT, older]);
    const sections = Array.from(el.querySelectorAll('cmn-list-section section'));

    expect(sections).toHaveLength(2);
    expect(sections.map(s => s.querySelector('h2')?.textContent?.trim())).toEqual([
      'Tue 22 Sept',
      'Sun 20 Sept',
    ]);
    expect(sections[0].querySelectorAll('[data-testid="alert-row"]')).toHaveLength(1);
  });

  it('mutes a resolved alert by recolour, fading only its icon, never the whole row', () => {
    const resolved = {...ALERT, id: 'a2', isResolved: true} as unknown as Alert;
    const {el} = render('all', 2, [ALERT, resolved]);
    const [open, done] = Array.from(el.querySelectorAll('[data-testid="alert-row"]'));

    expect(done.getAttribute('data-resolved')).toBe('true');
    expect(done.className).toContain(RESOLVED_ALERT_ROW_CLASS);
    expect(done.classList.contains('opacity-60')).toBe(false);
    expect(open.className).not.toContain(RESOLVED_ALERT_ROW_CLASS);
  });
});
