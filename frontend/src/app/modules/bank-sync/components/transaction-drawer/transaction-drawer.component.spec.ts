import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {CMN_DRAWER_DATA, CmnDrawerRef} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {type GlobalTransactionDto} from '../../models/transaction/transaction.model';
import {TransactionDrawerComponent} from './transaction-drawer.component';

function render() {
  const tx = {
    accountId: 'acc-1',
    bankName: 'Monobank',
    description: 'Coffee',
    amount: -4,
    currency: 'EUR',
    date: '2026-08-12',
    isPending: false,
    merchantCategory: 'groceries',
    transactionType: 'debit',
  } as unknown as GlobalTransactionDto;
  const drawerRef = {close: vi.fn()};
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(withXhr()),
      provideHttpClientTesting(),
      provideApiBaseUrl('http://localhost/api/v1'),
      {provide: CMN_DRAWER_DATA, useValue: tx},
      {provide: CmnDrawerRef, useValue: drawerRef},
    ],
  });
  const fixture = TestBed.createComponent(TransactionDrawerComponent);
  fixture.detectChanges();
  return {drawerRef, el: fixture.nativeElement as HTMLElement};
}

describe('TransactionDrawerComponent drill links', () => {
  it('links the account to the ledger filtered by that account and closes the drawer', () => {
    const {el, drawerRef} = render();
    const link = el.querySelector<HTMLAnchorElement>('[data-testid="drawer-account-link"]');

    expect(link?.getAttribute('href')).toBe('/transactions?account=acc-1');
    link?.click();
    expect(drawerRef.close).toHaveBeenCalled();
  });

  it('links the category to the ledger filtered by that category', () => {
    const {el} = render();
    const link = el.querySelector<HTMLAnchorElement>('[data-testid="drawer-category-link"]');

    expect(link?.getAttribute('href')).toBe('/transactions?category=groceries');
  });
});
