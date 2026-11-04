import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {ASYNC_STATE_SHOW_DELAY_MS, CmnDialogService} from '@lifekit-hq/ui';
import {onTestFinished} from 'vitest';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {type Subscription} from '../../models/subscription/subscription.model';
import {SubscriptionsStore} from '../../store/subscriptions/subscriptions.store';
import {SubscriptionsComponent} from './subscriptions.component';
import {NO_ACTIVE_SUBSCRIPTIONS_COPY} from './subscriptions.constants';

describe('SubscriptionsComponent', () => {
  it('routes the dismiss menu action through the confirmation dialog', () => {
    TestBed.configureTestingModule({});
    const component = TestBed.runInInjectionContext(() =>
      Object.create(SubscriptionsComponent.prototype)
    ) as SubscriptionsComponent;
    const dismiss = vi.spyOn(component, 'dismiss').mockImplementation(() => undefined);
    const sub = {id: 's1', merchantName: 'Netflix'} as unknown as Subscription;

    component.onSubscriptionAction('dismiss', sub);
    component.onSubscriptionAction('other', sub);

    expect(dismiss).toHaveBeenCalledTimes(1);
    expect(dismiss).toHaveBeenCalledWith(sub);
  });

  it('routes the link menu action to the transaction picker for a subscription and an installment', () => {
    TestBed.configureTestingModule({});
    const component = TestBed.runInInjectionContext(() =>
      Object.create(SubscriptionsComponent.prototype)
    ) as SubscriptionsComponent;
    const openLink = vi.spyOn(component, 'openLink').mockImplementation(() => undefined);
    const sub = {id: 's1', merchantName: 'Netflix'} as unknown as Subscription;

    component.onSubscriptionAction('link', sub);
    component.onInstallmentAction('link', sub);

    expect(openLink).toHaveBeenCalledTimes(2);
    expect(openLink).toHaveBeenCalledWith(sub);
  });

  it('offers Link transaction only on rows that follow no charges', () => {
    TestBed.configureTestingModule({});
    const component = TestBed.runInInjectionContext(() =>
      Object.create(SubscriptionsComponent.prototype)
    ) as SubscriptionsComponent;

    const ids = (items: {id: string}[]) => items.map(i => i.id);

    const row = {isManual: false, merchantName: 'Netflix'};

    expect(ids(component.subscriptionMenuItems({...row, isTracked: true}))).not.toContain('link');
    expect(ids(component.subscriptionMenuItems({...row, isTracked: false}))).toContain('link');
    expect(ids(component.installmentMenuItems({...row, isTracked: true}))).not.toContain('link');
    expect(ids(component.installmentMenuItems({...row, isTracked: false}))).toContain('link');
  });

  it('offers View charges only on rows whose name the ledger search can find', () => {
    TestBed.configureTestingModule({});
    const component = TestBed.runInInjectionContext(() =>
      Object.create(SubscriptionsComponent.prototype)
    ) as SubscriptionsComponent;
    const ids = (items: {id: string}[]) => items.map(i => i.id);
    const detected = {isManual: false, merchantName: 'Netflix'};
    const manual = {isManual: true, merchantName: 'Car loan'};

    for (const isTracked of [true, false]) {
      expect(ids(component.subscriptionMenuItems({...detected, isTracked}))).toContain('charges');
      expect(ids(component.installmentMenuItems({...detected, isTracked}))).toContain('charges');
      expect(ids(component.subscriptionMenuItems({...manual, isTracked}))).not.toContain('charges');
      expect(ids(component.installmentMenuItems({...manual, isTracked}))).not.toContain('charges');
    }
  });

  it('opens the ledger searched by merchant from the View charges action', () => {
    TestBed.configureTestingModule({providers: [provideRouter([])]});
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const component = TestBed.runInInjectionContext(() => {
      const c = Object.create(SubscriptionsComponent.prototype) as {router: Router};
      c.router = TestBed.inject(Router);
      return c as unknown as SubscriptionsComponent;
    });
    const sub = {id: 's1', merchantName: 'Netflix', isManual: false} as unknown as Subscription;

    component.onSubscriptionAction('charges', sub);
    component.onInstallmentAction('charges', sub);

    expect(navigate).toHaveBeenCalledTimes(2);
    expect(navigate).toHaveBeenCalledWith(['/transactions'], {queryParams: {q: 'Netflix'}});
  });

  it('does not navigate for a row with no searchable name', () => {
    TestBed.configureTestingModule({providers: [provideRouter([])]});
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const component = TestBed.runInInjectionContext(() => {
      const c = Object.create(SubscriptionsComponent.prototype) as {router: Router};
      c.router = TestBed.inject(Router);
      return c as unknown as SubscriptionsComponent;
    });

    component.viewCharges({isTracked: true, isManual: true, merchantName: 'Car loan'});

    expect(navigate).not.toHaveBeenCalled();
  });

  it('sorts through the store when a sort menu item is chosen', () => {
    TestBed.configureTestingModule({});
    const component = TestBed.runInInjectionContext(() =>
      Object.create(SubscriptionsComponent.prototype)
    ) as SubscriptionsComponent;
    const setSort = vi.spyOn(component, 'setSort').mockImplementation(() => undefined);

    component.onSortSelect('amount');

    expect(setSort).toHaveBeenCalledWith('amount');
  });
});

describe('SubscriptionsComponent async states', () => {
  const NETFLIX = {
    id: 's1',
    merchantName: 'Netflix',
    cadence: 'monthly',
    monthlyEquivalent: 10,
    currency: 'EUR',
    nextExpectedDate: '2026-09-01',
    status: 'active',
    kind: 'subscription',
    isManual: false,
    isTracked: true,
  } as unknown as Subscription;

  function render(
    overrides: {
      isLoading?: boolean;
      loadErrorMessage?: string;
      subscriptions?: Subscription[];
      activeSubscriptions?: Subscription[];
      hasInsufficientHistory?: boolean;
      offline?: boolean;
    } = {}
  ) {
    const subscriptions = overrides.subscriptions ?? [];
    const store = {
      isLoadLoading: signal(overrides.isLoading ?? false),
      loadErrorMessage: signal(overrides.loadErrorMessage ?? ''),
      subscriptions: signal(subscriptions),
      hasInsufficientHistory: signal(overrides.hasInsufficientHistory ?? false),
      addAlertMessage: signal(''),
      summary: signal(null),
      sort: signal('date'),
      activeSections: signal(
        subscriptions.length > 0 ? [{id: 'upcoming', label: '', items: subscriptions}] : []
      ),
      activeSubscriptions: signal(overrides.activeSubscriptions ?? subscriptions),
      activeInstallments: signal<Subscription[]>([]),
      sortedInstallments: signal<Subscription[]>([]),
      completedInstallments: signal<Subscription[]>([]),
      dismissedSubscriptions: signal<Subscription[]>([]),
      sortedDismissed: signal<Subscription[]>([]),
      load: vi.fn(),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {provide: CmnDialogService, useValue: {open: vi.fn()}},
        {provide: ConnectivityService, useValue: {offline: signal(overrides.offline ?? false)}},
      ],
    });
    TestBed.overrideComponent(SubscriptionsComponent, {
      set: {providers: [{provide: SubscriptionsStore, useValue: store}]},
    });
    const fixture = TestBed.createComponent(SubscriptionsComponent);
    fixture.detectChanges();
    return {fixture, store, host: fixture.nativeElement as HTMLElement};
  }

  function retryButton(host: HTMLElement) {
    return Array.from(host.querySelectorAll('button')).find(b => b.textContent?.trim() === 'Retry');
  }

  it('shows a skeleton instead of the empty copy while the first load runs', () => {
    vi.useFakeTimers();
    onTestFinished(() => {
      vi.useRealTimers();
    });
    const {fixture, host} = render({isLoading: true});
    vi.advanceTimersByTime(ASYNC_STATE_SHOW_DELAY_MS);
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="subscriptions-skeleton"]')).not.toBeNull();
    expect(host.textContent).not.toContain('No active subscriptions yet');
  });

  it('shows the load error, never the empty copy, and reloads on Retry', () => {
    const {host, store, fixture} = render({loadErrorMessage: 'Failed to load subscriptions.'});

    expect(host.textContent).toContain('Failed to load subscriptions.');
    expect(host.textContent).not.toContain('No active subscriptions yet');

    retryButton(host)?.click();
    fixture.detectChanges();

    expect(store.load).toHaveBeenCalledTimes(1);
  });

  it('shows the empty state when nothing is tracked', () => {
    const {host} = render();

    expect(host.textContent).toContain('No active subscriptions yet');
    expect(host.textContent).toContain(
      'Recurring charges show up here once they are detected, or add one yourself.'
    );
  });

  it('opens the add dialog from the empty state', () => {
    const {host, fixture} = render();
    const openAdd = vi
      .spyOn(fixture.componentInstance, 'openAdd')
      .mockImplementation(() => undefined);

    Array.from(host.querySelectorAll('button'))
      .find(b => b.textContent?.trim() === 'Add subscription')
      ?.click();

    expect(openAdd).toHaveBeenCalledOnce();
  });

  it('keeps the rows under the load error when a reload fails while offline', () => {
    const {host} = render({
      subscriptions: [NETFLIX],
      loadErrorMessage: 'Failed to load subscriptions.',
      offline: true,
    });

    expect(host.textContent).toContain('Failed to load subscriptions.');
    expect(host.textContent).toContain('Netflix');
  });

  it('says there is nothing active when only installments or dismissed rows are tracked', () => {
    const {host} = render({subscriptions: [NETFLIX], activeSubscriptions: []});

    expect(host.textContent).toContain(NO_ACTIVE_SUBSCRIPTIONS_COPY.message);
    expect(host.textContent).toContain(NO_ACTIVE_SUBSCRIPTIONS_COPY.subMessage);
  });

  it('explains the missing history instead of the empty state when there is too little', () => {
    const {host} = render({hasInsufficientHistory: true});

    expect(host.textContent).toContain('Not enough transaction history');
    expect(host.textContent).not.toContain('Recurring charges show up here');
  });

  describe('latest price change', () => {
    const withChange = (oldAmount: number, newAmount: number, changePercent: number) =>
      ({
        ...NETFLIX,
        latestPriceChange: {oldAmount, newAmount, changePercent, chargeDate: '2026-06-15'},
      }) as unknown as Subscription;

    const line = (host: HTMLElement) => host.querySelector('[data-testid="price-change"]');

    it('flags an increase with its percent and date, and names both amounts on hover', () => {
      const {host} = render({subscriptions: [withChange(9.99, 12.99, 30.03)]});

      const el = line(host);
      expect(el?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Up 30% · 15 Jun');
      expect(el?.classList.contains('text-status-warning')).toBe(true);
      expect(el?.getAttribute('title')).toBe('Was €9.99, now €12.99');
    });

    it('shows a decrease as a positive percent in the success colour', () => {
      const {host} = render({subscriptions: [withChange(12.99, 9.99, -23.09)]});

      const el = line(host);
      expect(el?.textContent?.replace(/\s+/g, ' ').trim()).toBe('Down 23% · 15 Jun');
      expect(el?.classList.contains('text-status-success')).toBe(true);
    });

    it('shows nothing for a subscription that never changed price', () => {
      const unchanged: Subscription = {...NETFLIX, latestPriceChange: null};
      const {host} = render({subscriptions: [unchanged]});

      expect(line(host)).toBeNull();
    });
  });

  it('lists the subscriptions once loaded', () => {
    const {host} = render({subscriptions: [NETFLIX]});

    expect(host.textContent).toContain('Netflix');
    expect(host.querySelector('[data-testid="subscriptions-skeleton"]')).toBeNull();
    expect(host.textContent).not.toContain('No active subscriptions yet');
  });
});
