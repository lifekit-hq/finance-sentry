import {TestBed} from '@angular/core/testing';
import {provideRouter, Router} from '@angular/router';

import {type Subscription} from '../../models/subscription/subscription.model';
import {SubscriptionsComponent} from './subscriptions.component';

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
});
