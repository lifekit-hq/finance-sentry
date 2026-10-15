import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {type TransactionFilterSelection} from '../../models/transaction/transaction-filter.model';
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
    const {fixture} = setup({from: '2026-06-01', to: '2026-06-20', type: 'debit', categories: []});
    expect(fixture.componentInstance.isCustom()).toBe(true);
    expect(fixture.componentInstance.rangeControl.value).toEqual({
      from: '2026-06-01',
      to: '2026-06-20',
    });
  });

  it('emits the edited draft on Show results', () => {
    const {applied, press, fixture} = setup({from: null, to: null, type: null, categories: []});
    press('sheet-period-3m');
    press('type-out');
    press('category-FOOD');
    fixture.detectChanges();
    press('filter-apply');
    expect(applied).toEqual([
      {...LedgerPeriodUtils.dates('3m'), type: 'debit', categories: ['FOOD']},
    ]);
  });

  it('Reset empties the draft without emitting', () => {
    const {applied, press, fixture} = setup({
      from: null,
      to: null,
      type: 'credit',
      categories: ['FOOD'],
    });
    press('filter-reset');
    expect(applied).toHaveLength(0);
    expect(fixture.componentInstance.draft()).toEqual({
      from: null,
      to: null,
      type: null,
      categories: [],
    });
  });
});
