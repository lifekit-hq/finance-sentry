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

    expect(ids(component.subscriptionMenuItems({isTracked: true}))).not.toContain('link');
    expect(ids(component.subscriptionMenuItems({isTracked: false}))).toContain('link');
    expect(ids(component.installmentMenuItems({isTracked: true}))).not.toContain('link');
    expect(ids(component.installmentMenuItems({isTracked: false}))).toContain('link');
  });

  it('offers View charges on every subscription and installment row', () => {
    TestBed.configureTestingModule({});
    const component = TestBed.runInInjectionContext(() =>
      Object.create(SubscriptionsComponent.prototype)
    ) as SubscriptionsComponent;
    const ids = (items: {id: string}[]) => items.map(i => i.id);

    for (const isTracked of [true, false]) {
      expect(ids(component.subscriptionMenuItems({isTracked}))).toContain('charges');
      expect(ids(component.installmentMenuItems({isTracked}))).toContain('charges');
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
    const sub = {id: 's1', merchantName: 'Netflix'} as unknown as Subscription;

    component.onSubscriptionAction('charges', sub);
    component.onInstallmentAction('charges', sub);

    expect(navigate).toHaveBeenCalledTimes(2);
    expect(navigate).toHaveBeenCalledWith(['/transactions'], {queryParams: {q: 'Netflix'}});
  });
});
