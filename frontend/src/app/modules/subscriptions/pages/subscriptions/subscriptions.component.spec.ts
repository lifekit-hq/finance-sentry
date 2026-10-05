import {TestBed} from '@angular/core/testing';

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
});
