import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap, provideRouter, Router} from '@angular/router';
import {BehaviorSubject} from 'rxjs';

import {TransactionLedgerStore} from '../../store/transaction-ledger/transaction-ledger.store';
import {TransactionLedgerComponent} from './transaction-ledger.component';

function setup(type: string | null, extra: Record<string, string> = {}) {
  const params$ = new BehaviorSubject(convertToParamMap({...(type ? {type} : {}), ...extra}));
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
    applyType: vi.fn(),
    applyDateRange: vi.fn(),
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
  return {chip, selected, navigate, root, store};
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

  it('drives the store type filter from the type query param', () => {
    const {store} = setup('credit');
    const followed = store.applyType.mock.calls[0][0] as () => unknown;
    expect(followed()).toBe('credit');
  });

  it('treats an unknown type query param as All', () => {
    const {store, selected} = setup('bogus');
    const followed = store.applyType.mock.calls[0][0] as () => unknown;
    expect(followed()).toBeNull();
    expect(selected('type-all')).toBe('true');
  });

  it('shows no duplicate removable Type chip for a deep-linked type', () => {
    const {root} = setup('credit');
    expect(root.textContent).not.toContain('Type:');
  });

  it('preselects the control from a deep-linked type', () => {
    const {selected} = setup('credit');
    expect(selected('type-in')).toBe('true');
    expect(selected('type-all')).toBe('false');
    expect(selected('type-out')).toBe('false');
  });
});

describe('TransactionLedgerComponent date range', () => {
  it('shows a clearable chip and drives the store from from/to query params', () => {
    const {root, store, navigate} = setup(null, {from: '2026-06-01', to: '2026-08-31'});
    const chip = root.querySelector('[data-testid="date-range-chip"]') as HTMLElement;
    expect(chip.textContent).toContain('2026-06-01 – 2026-08-31');
    expect(store.applyDateRange).toHaveBeenCalled();
    chip.querySelector('button')?.click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: {from: null, to: null},
      queryParamsHandling: 'merge',
    });
  });

  it('ignores malformed dates', () => {
    const {root} = setup(null, {from: 'garbage', to: '2026-8-1'});
    expect(root.querySelector('[data-testid="date-range-chip"]')).toBeNull();
  });
});
