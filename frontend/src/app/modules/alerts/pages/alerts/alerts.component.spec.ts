import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {CmnDrawerService} from '@lifekit-hq/ui';
import {of} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {AlertFilterSheetComponent} from '../../components/alert-filter-sheet/alert-filter-sheet.component';
import {type Alert, type AlertFilter} from '../../models/alert/alert.model';
import {AlertsStore} from '../../store/alerts/alerts.store';
import {AlertsComponent} from './alerts.component';
import {RESOLVED_ALERT_ROW_CLASS} from './alerts.constants';

const UNREAD_COUNT = 3;
const PAGE_SIZE = 20;
const TOTAL_COUNT = 45;

const drawerOpen = vi.fn();

function render(
  filter: AlertFilter = 'all',
  unreadCount = 0,
  alerts: Alert[] = [],
  async: {isLoading?: boolean; errorMessage?: Nullable<string>} = {}
) {
  const store = {
    alerts: signal(alerts),
    filter: signal(filter),
    unreadCount: signal(unreadCount),
    pageSize: signal(PAGE_SIZE),
    totalCount: signal(TOTAL_COUNT),
    currentPage: signal(1),
    hasPreviousPage: signal(false),
    hasNextPage: signal(true),
    totalPages: signal(3),
    setFilter: vi.fn(),
    setPageSize: vi.fn(),
    isLoading: signal(async.isLoading ?? false),
    errorMessage: signal<Nullable<string>>(async.errorMessage ?? null),
    query: signal({filter, page: 1, pageSize: PAGE_SIZE}),
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

describe('AlertsComponent filter flow', () => {
  beforeEach(() => drawerOpen.mockReset());

  it('shows no applied filter and no badge count for the unfiltered list', () => {
    const {el, component} = render('all', UNREAD_COUNT);

    expect(component.activeFilterCount()).toBe(0);
    expect(el.querySelector('[data-testid="alerts-applied-filters"]')).toBeNull();
    expect(el.querySelector('lk-segmented[label="Filter alerts"]')).toBeNull();
  });

  it('counts the filter and shows it as a dismissible chip with Clear', () => {
    const {el, component} = render('warning');

    expect(component.activeFilterCount()).toBe(1);
    expect(el.querySelector('[data-testid="alerts-chip-filter"]')?.getAttribute('label')).toBe(
      'Warnings'
    );
    expect(el.querySelector('[data-testid="alerts-filter-clear"]')).not.toBeNull();
  });

  it('opens the filter sheet with the current filter and applies the chosen one', () => {
    drawerOpen.mockReturnValue({afterClosed: () => of('unread')});
    const {component, store} = render('error');

    component.openFilters();

    expect(drawerOpen).toHaveBeenCalledWith(
      AlertFilterSheetComponent,
      expect.objectContaining({data: 'error'})
    );
    expect(store.setFilter).toHaveBeenCalledWith('unread');
  });

  it('leaves the filter alone when the sheet is dismissed', () => {
    drawerOpen.mockReturnValue({afterClosed: () => of(undefined)});
    const {component, store} = render('error');

    component.openFilters();

    expect(store.setFilter).not.toHaveBeenCalled();
  });

  it('clears the filter from the chip and from Clear', () => {
    const {el, store} = render('info');

    (el.querySelector('[data-testid="alerts-filter-clear"] button') as HTMLButtonElement).click();
    el.querySelector('[data-testid="alerts-chip-filter"]')?.dispatchEvent(
      new CustomEvent('lk-dismissible-chip-remove')
    );

    expect(store.setFilter).toHaveBeenCalledTimes(2);
    expect(store.setFilter).toHaveBeenCalledWith('all');
  });

  it('converts the chosen page size back to a number', () => {
    const {component, store} = render();

    component.onPageSizeChange(new CustomEvent('lk-segmented-change', {detail: {value: '50'}}));

    expect(store.setPageSize).toHaveBeenCalledWith(50);
  });

  it('offers the page sizes as strings labelled "N per page"', () => {
    const {component} = render();

    expect(component.pageSizeOptions).toEqual([
      {value: '20', label: '20 per page'},
      {value: '50', label: '50 per page'},
      {value: '100', label: '100 per page'},
    ]);
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

  it('shows a skeleton, not the empty copy, while the page loads', () => {
    const {el} = render('all', 0, [], {isLoading: true});

    expect(el.querySelector('[data-testid="alerts-skeleton"]')).not.toBeNull();
    expect(el.textContent).not.toContain('All your accounts are healthy.');
  });

  it('shows the load error and reloads the current query on Retry', () => {
    const {el, store, fixture} = render('warning', 0, [], {errorMessage: 'Failed to load alerts.'});

    expect(el.textContent).toContain('Failed to load alerts.');
    expect(el.textContent).not.toContain('Try a different filter.');

    retryButton(el)?.click();
    fixture.detectChanges();

    expect(store.load).toHaveBeenCalledWith({filter: 'warning', page: 1, pageSize: PAGE_SIZE});
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
