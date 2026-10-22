import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {of} from 'rxjs';
import {describe, expect, it, vi} from 'vitest';

import {type Alert, type AlertsPageResponse} from '../../models/alert/alert.model';
import {AlertsService} from '../../services/alerts.service';
import {AlertsStore} from '../../store/alerts/alerts.store';
import {TopAlertComponent} from './top-alert.component';

const ALERT: Alert = {
  id: 'a',
  type: 'LowBalance',
  severity: 'Warning',
  title: 'Low balance',
  message: 'Balance dropped',
  referenceId: null,
  referenceLabel: null,
  isRead: false,
  isResolved: false,
  createdAt: '2026-10-01T09:00:00Z',
  resolvedAt: null,
  occurrenceCount: 1,
  lastOccurredAt: '2026-10-01T09:00:00Z',
  appPath: '/accounts/list',
};

function render(items: Alert[], totalCount: number) {
  const markRead = vi.fn();
  const response: AlertsPageResponse = {
    items,
    totalCount,
    unreadCount: totalCount,
    page: 1,
    pageSize: 1,
    totalPages: totalCount,
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      {
        provide: AlertsService,
        useValue: {getAlerts: () => of(response)},
      },
      {provide: AlertsStore, useValue: {markRead}},
    ],
  });
  const fixture = TestBed.createComponent(TopAlertComponent);
  fixture.detectChanges();
  const navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  return {el: fixture.nativeElement as HTMLElement, markRead, navigateByUrl};
}

const text = (el: Element | null): string => el?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

describe('TopAlertComponent', () => {
  it('shows the newest unread alert and how many more wait in Alerts', () => {
    const {el} = render([ALERT], 3);

    expect(text(el.querySelector('h2'))).toBe('Worth a look');
    expect(el.querySelector('cmn-alert-item')).not.toBeNull();
    expect(text(el.querySelector('[data-testid="top-alert-more"]'))).toBe('2 more in Alerts ›');
    expect(el.querySelector('[data-testid="top-alert-more"]')?.getAttribute('href')).toBe(
      '/alerts'
    );
  });

  it('links plainly to Alerts when the one shown is the only unread', () => {
    const {el} = render([ALERT], 1);

    expect(text(el.querySelector('[data-testid="top-alert-more"]'))).toBe('Alerts ›');
  });

  it('renders nothing when every alert is read', () => {
    const {el} = render([], 0);

    expect(el.querySelector('[data-testid="top-alert"]')).toBeNull();
  });

  it('marks the alert read and opens what it is about', () => {
    const {el, markRead, navigateByUrl} = render([ALERT], 1);

    el.querySelector<HTMLButtonElement>('[data-testid="top-alert-item"] button')?.click();

    expect(markRead).toHaveBeenCalledExactlyOnceWith('a');
    expect(navigateByUrl).toHaveBeenCalledExactlyOnceWith('/accounts/list');
  });
});
