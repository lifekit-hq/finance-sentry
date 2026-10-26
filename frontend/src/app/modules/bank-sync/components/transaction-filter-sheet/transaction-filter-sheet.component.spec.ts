import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {
  EMPTY_TRANSACTION_FILTER,
  type TransactionFilterSelection,
} from '../../models/transaction/transaction-filter.model';
import {LedgerPeriodUtils} from '../../utils/ledger-period.utils';
import {TransactionFilterSheetComponent} from './transaction-filter-sheet.component';

function setup(initial: TransactionFilterSelection) {
  TestBed.configureTestingModule({
    providers: [
      {
        provide: CategoryStore,
        useValue: {
          labelMap: signal({}),
          filterOptions: signal([{value: 'FOOD', label: 'Food'}]),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(TransactionFilterSheetComponent);
  fixture.componentRef.setInput('initial', initial);
  fixture.detectChanges();
  const applied: TransactionFilterSelection[] = [];
  fixture.componentInstance.applied.subscribe(s => applied.push(s));
  const root = fixture.nativeElement as HTMLElement;
  const press = (id: string) =>
    root.querySelector<HTMLButtonElement>(`[data-testid="${id}"] button`)?.click();
  return {fixture, applied, press};
}

describe('TransactionFilterSheetComponent', () => {
  it('starts from the selection it is given, custom dates included', () => {
    const {fixture} = setup({
      from: '2026-06-01',
      to: '2026-06-20',
      type: 'debit',
      categories: [],
      accountId: null,
    });
    expect(fixture.componentInstance.isCustom()).toBe(true);
    expect(fixture.componentInstance.rangeControl.value).toEqual({
      from: '2026-06-01',
      to: '2026-06-20',
    });
  });

  it('emits the edited draft on Show results', () => {
    const {applied, press, fixture} = setup({
      from: null,
      to: null,
      type: null,
      categories: [],
      accountId: null,
    });
    press('sheet-period-3m');
    press('type-out');
    press('category-FOOD');
    fixture.detectChanges();
    press('filter-apply');
    expect(applied).toEqual([
      {...LedgerPeriodUtils.dates('3m'), type: 'debit', categories: ['FOOD'], accountId: null},
    ]);
  });

  it('Reset empties the draft without emitting', () => {
    const {applied, press, fixture} = setup({
      from: null,
      to: null,
      type: 'credit',
      categories: ['FOOD'],
      accountId: 'a1',
    });
    press('filter-reset');
    expect(applied).toHaveLength(0);
    expect(fixture.componentInstance.draft()).toEqual({
      from: null,
      to: null,
      type: null,
      categories: [],
      accountId: null,
    });
  });

  const TWO_ACCOUNTS = [
    {accountId: 'a1', label: 'Monzo'},
    {accountId: 'a2', label: 'AIB'},
  ];

  it('offers the Account section only with more than one account', () => {
    const {fixture} = setup({...EMPTY_TRANSACTION_FILTER});
    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('[data-testid="account-all"]')).toBeNull();
    fixture.componentRef.setInput('accounts', TWO_ACCOUNTS.slice(0, 1));
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="account-all"]')).toBeNull();
    fixture.componentRef.setInput('accounts', TWO_ACCOUNTS);
    fixture.detectChanges();
    expect(root.querySelector('[data-testid="account-all"]')).not.toBeNull();
  });

  it('applies the picked account only on Show results', () => {
    const {fixture, applied, press} = setup({...EMPTY_TRANSACTION_FILTER});
    fixture.componentRef.setInput('accounts', TWO_ACCOUNTS);
    fixture.detectChanges();
    press('account-a2');
    expect(applied).toHaveLength(0);
    press('filter-apply');
    expect(applied).toEqual([{...EMPTY_TRANSACTION_FILTER, accountId: 'a2'}]);
  });
});
