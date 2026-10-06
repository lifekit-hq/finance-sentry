import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {describe, expect, it} from 'vitest';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {FlowBreakdownStore} from '../../store/flow-breakdown/flow-breakdown.store';
import {FlowBreakdownComponent} from './flow-breakdown.component';

const GROUP = {
  bucket: 'spending',
  label: 'Spending',
  note: 'counted',
  counted: true,
  totalUsd: 100,
  items: [
    {
      transactionId: 't1',
      accountId: 'a1',
      bankName: 'Monobank',
      accountLast4: '1234',
      currency: 'UAH',
      amount: 4100,
      amountUsd: 100,
      date: '2026-09-12',
      description: 'Silpo groceries',
      merchantName: null,
      category: null,
      direction: 'out',
      bucket: 'spending',
      counterpartyName: null,
      flowRole: null,
    },
  ],
};

function render(
  group: Omit<typeof GROUP, 'items'> & {items: object[]} = GROUP,
  range: Nullable<{from: Nullable<string>; to: string; months: number}> = null
) {
  const store = {
    month: signal('2026-09'),
    range: signal(range),
    isLoading: signal(false),
    isEmpty: signal(false),
    errorMessage: signal(null),
    accountChips: signal([]),
    accountFilter: signal(null),
    groups: signal([group]),
    incomeFormatted: signal('$0'),
    spendingFormatted: signal('$100'),
    investedFormatted: signal('$0'),
    savedFormatted: signal('$0'),
  };
  TestBed.configureTestingModule({
    providers: [provideRouter([]), {provide: CategoryStore, useValue: {labelMap: signal({})}}],
  });
  TestBed.overrideComponent(FlowBreakdownComponent, {
    set: {providers: [{provide: FlowBreakdownStore, useValue: store}]},
  });
  const fixture = TestBed.createComponent(FlowBreakdownComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('FlowBreakdownComponent phone layout', () => {
  it('renders list rows with description, bank·date subtitle and both amounts below md', () => {
    const rows = render().querySelector('[data-testid="breakdown-rows"]');
    expect(rows?.className).toContain('md:hidden');
    const row = rows?.querySelector('cmn-list-item-row');
    expect(row?.textContent).toContain('Silpo groceries');
    expect(row?.textContent).toContain('Monobank ••1234 · Sep 12');
    expect(row?.textContent).toContain('−');
    expect(row?.textContent).toContain('$100');
  });

  it('keeps the counterparty tag, or the category tag, on phone rows', () => {
    const withCounterparty = {...GROUP.items[0], counterpartyName: 'Alice'};
    const withCategory = {...GROUP.items[0], category: 'groceries'};
    const el = render({...GROUP, items: [withCounterparty, withCategory]});
    const rows = el.querySelectorAll('[data-testid="breakdown-rows"] cmn-list-item-row');
    expect(rows[0].querySelector('cmn-tag')?.textContent).toContain('Alice');
    expect(rows[1].querySelector('cmn-tag')?.textContent?.trim()).toBe('Groceries');
  });

  it('keeps the table for md and up only', () => {
    const table = render().querySelector('table');
    expect(table?.parentElement?.className).toContain('hidden');
    expect(table?.parentElement?.className).toContain('md:block');
  });

  it('uses the 16px phone gutter and two-column stats', () => {
    const el = render();
    expect(el.firstElementChild?.className).toContain('page-container');
    expect(el.querySelector('.grid')?.className).toContain('grid-cols-2');
    expect(el.firstElementChild?.className).not.toContain('overflow-auto');
  });
});

describe('FlowBreakdownComponent window mode', () => {
  it('names the window and drops the month stepper', () => {
    const el = render(GROUP, {from: '2026-10-01', to: '2026-10-17', months: 1});

    expect(el.querySelector('[data-testid="breakdown-window"]')?.textContent).toContain(
      'from Oct 1, 2026 to Oct 17, 2026'
    );
    expect(el.querySelector('cmn-month-stepper')).toBeNull();
  });

  it('words an all-time window as open-ended', () => {
    const el = render(GROUP, {from: null, to: '2026-10-17', months: 120});

    expect(el.querySelector('[data-testid="breakdown-window"]')?.textContent).toContain(
      'up to Oct 17, 2026'
    );
  });

  it('keeps the month stepper in month mode', () => {
    const el = render();

    expect(el.querySelector('cmn-month-stepper')).not.toBeNull();
    expect(el.querySelector('[data-testid="breakdown-window"]')).toBeNull();
  });
});
