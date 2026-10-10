import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {describe, expect, it, vi} from 'vitest';

import {type Alert, type AlertFilter} from '../../models/alert/alert.model';
import {AlertsStore} from '../../store/alerts/alerts.store';
import {AlertsComponent} from './alerts.component';

const UNREAD_COUNT = 3;
const PAGE_SIZE = 20;
const TOTAL_COUNT = 45;

type Segmented = HTMLElement & {options: {label: string; value: string}[]; value: string};

function render(filter: AlertFilter = 'all', unreadCount = 0, alerts: Alert[] = []) {
  const store = {
    alerts: signal(alerts),
    filter: signal(filter),
    unreadCount: signal(unreadCount),
    pageSize: signal(PAGE_SIZE),
    totalCount: signal(TOTAL_COUNT),
    currentPage: signal(1),
    hasPreviousPage: signal(false),
    hasNextPage: signal(true),
    pageCount: signal(3),
    setFilter: vi.fn(),
    setPageSize: vi.fn(),
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), {provide: AlertsStore, useValue: store}],
  });
  const fixture = TestBed.createComponent(AlertsComponent);
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  return {fixture, el, store, component: fixture.componentInstance};
}

function change(control: Element | null | undefined, value: string) {
  control?.dispatchEvent(new CustomEvent('lk-segmented-change', {detail: {value}}));
}

describe('AlertsComponent single-choice controls', () => {
  it('offers the filters in order with the unread count in its label', () => {
    const {el} = render('all', UNREAD_COUNT);
    const control = el.querySelector<Segmented>('lk-segmented[label="Filter alerts"]');

    expect(control?.options).toEqual([
      {value: 'all', label: 'All'},
      {value: 'unread', label: `Unread (${UNREAD_COUNT})`},
      {value: 'error', label: 'Errors'},
      {value: 'warning', label: 'Warnings'},
      {value: 'info', label: 'Info'},
    ]);
    expect(control?.value).toBe('all');
  });

  it('labels the unread filter plainly when nothing is unread', () => {
    const {el} = render();

    expect(
      el.querySelector<Segmented>('lk-segmented')?.options.find(o => o.value === 'unread')?.label
    ).toBe('Unread');
  });

  it('sets the filter through the store when the control changes', () => {
    const {el, store} = render();

    change(el.querySelector('lk-segmented[label="Filter alerts"]'), 'warning');

    expect(store.setFilter).toHaveBeenCalledWith('warning');
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
