import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap, provideRouter, Router} from '@angular/router';
import {BehaviorSubject} from 'rxjs';

import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
import {TransactionLedgerComponent} from './transaction-ledger.component';

function setup(type: string | null) {
  const params$ = new BehaviorSubject(convertToParamMap(type ? {type} : {}));
  const store = {
    accounts: signal([]),
    monthlyOutflow: signal(null),
    topCategory: signal(null),
    errorMessage: signal(null),
    isLoading: signal(false),
    isEmpty: signal(true),
    hasActiveFilter: signal(false),
    hasMore: signal(false),
    transactions: signal([]),
    applyAccount: () => undefined,
    applySearch: () => undefined,
  };
  TestBed.overrideComponent(TransactionLedgerComponent, {
    set: {providers: [{provide: TransactionLedgerStore, useValue: store}]},
  });
  TestBed.configureTestingModule({
    providers: [provideRouter([]), {provide: ActivatedRoute, useValue: {queryParamMap: params$}}],
  });
  const router = TestBed.inject(Router);
  const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
  const fixture = TestBed.createComponent(TransactionLedgerComponent);
  fixture.detectChanges();
  const root = fixture.nativeElement as HTMLElement;
  const chip = (id: string) => root.querySelector(`[data-testid="${id}"]`) as HTMLElement;
  const selected = (id: string) => chip(id).querySelector('button')?.getAttribute('aria-pressed');
  return {chip, selected, navigate, root};
}

describe('TransactionLedgerComponent type control', () => {
  it('sets type=credit when In is selected', () => {
    const {chip, navigate} = setup(null);
    chip('type-in').querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {type: 'credit'},
      queryParamsHandling: 'merge',
    });
  });

  it('sets type=debit when Out is selected', () => {
    const {chip, navigate} = setup(null);
    chip('type-out').querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {type: 'debit'},
      queryParamsHandling: 'merge',
    });
  });

  it('clears the type when All is selected', () => {
    const {chip, navigate} = setup('debit');
    chip('type-all').querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {type: null},
      queryParamsHandling: 'merge',
    });
  });

  it('preselects the control from a deep-linked type', () => {
    const {selected} = setup('credit');
    expect(selected('type-in')).toBe('true');
    expect(selected('type-all')).toBe('false');
    expect(selected('type-out')).toBe('false');
  });
});
