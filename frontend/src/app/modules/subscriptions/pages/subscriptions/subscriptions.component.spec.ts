import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';
import {CmnDialogService} from '@lifekit-hq/ui';

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

  it('sorts through the store when the sort control changes', () => {
    TestBed.configureTestingModule({});
    const component = TestBed.runInInjectionContext(() =>
      Object.create(SubscriptionsComponent.prototype)
    ) as SubscriptionsComponent;
    const setSort = vi.spyOn(component, 'setSort').mockImplementation(() => undefined);

    component.onSortChange(new CustomEvent('lk-segmented-change', {detail: {value: 'amount'}}));

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
      isLoading: signal(overrides.isLoading ?? false),
      loadErrorMessage: signal(overrides.loadErrorMessage ?? ''),
      subscriptions: signal(subscriptions),
      hasInsufficientHistory: signal(overrides.hasInsufficientHistory ?? false),
      addErrorMessage: signal(''),
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
    const {host} = render({isLoading: true});

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

  it('lists the subscriptions once loaded', () => {
    const {host} = render({subscriptions: [NETFLIX]});

    expect(host.textContent).toContain('Netflix');
    expect(host.querySelector('[data-testid="subscriptions-skeleton"]')).toBeNull();
    expect(host.textContent).not.toContain('No active subscriptions yet');
  });
});
