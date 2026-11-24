import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type BookIncome, type IncomeWindow} from '../../models/book-income/book-income.model';
import {BookIncomeStore} from '../../store/book-income/book-income.store';
import {BookIncomeComponent} from './book-income.component';

const win = (gross: number, withholding: number): IncomeWindow => ({
  gross,
  withholding,
  net: gross - withholding,
});

const INCOME: BookIncome = {
  asOf: '2026-11-24',
  ttm: win(120, 18),
  ytd: win(100, 15),
  forwardAnnualUsd: 140,
  yieldOnCost: 0.057,
  unvaluedReceipts: 0,
  holdings: [
    {
      symbol: 'KO',
      kinds: [0],
      ttm: win(120, 18),
      ytd: win(100, 15),
      forwardAnnualUsd: 140,
      yieldOnCost: 0.057,
      nextExDividend: '2026-12-01',
    },
  ],
};

describe('BookIncomeComponent', () => {
  let income: ReturnType<typeof signal<BookIncome>>;
  let hasIncome: ReturnType<typeof signal<boolean>>;
  let yieldLabel: ReturnType<typeof signal<string | null>>;

  const render = (): HTMLElement => {
    const fixture = TestBed.createComponent(BookIncomeComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  const text = (el: HTMLElement, testId: string): string =>
    el.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

  beforeEach(async () => {
    income = signal(INCOME);
    hasIncome = signal(true);
    yieldLabel = signal<string | null>('5.7%');
    const store = {
      status: signal<AsyncStatus>('success'),
      errorMessage: signal(''),
      income,
      hasIncome,
      yieldOnCostLabel: yieldLabel,
      holdings: signal(INCOME.holdings),
      load: vi.fn(),
    };
    await TestBed.configureTestingModule({imports: [BookIncomeComponent]})
      .overrideComponent(BookIncomeComponent, {
        set: {providers: [{provide: BookIncomeStore, useValue: store}]},
      })
      .compileComponents();
  });

  it('shows net income, the tax withheld, the forward estimate and yield on cost', () => {
    const el = render();

    expect(text(el, 'book-income-ttm')).toBe('$102.00');
    expect(text(el, 'book-income-ytd')).toBe('$85.00');
    expect(text(el, 'book-income-ttm-row')).toContain('Tax withheld $18.00');
    expect(text(el, 'book-income-forward')).toBe('$140.00');
    expect(text(el, 'book-income-yield')).toBe('5.7%');
  });

  it('shows a holding with its TTM, YTD and next ex-dividend date together', () => {
    const el = render();

    expect(text(el, 'book-income-holding-KO')).toContain('Year to date $85.00');
    expect(text(el, 'book-income-holding-KO-ex-dividend')).toBe('Next ex-dividend 1 Dec 2026');
    expect(text(el, 'book-income-holding-KO-ttm')).toContain('$102.00');
    expect(text(el, 'book-income-holding-KO-ttm')).toBe('$102.00 after $18.00 tax');
  });

  it('says "Not available" for a missing estimate and yield, never zero', () => {
    income.set({...INCOME, forwardAnnualUsd: null, yieldOnCost: null});
    yieldLabel.set(null);
    const el = render();

    expect(text(el, 'book-income-forward')).toBe('Not available');
    expect(text(el, 'book-income-yield')).toBe('Not available');
  });

  it('renders no figures for a book with no income and no payers', () => {
    hasIncome.set(false);
    const el = render();

    expect(el.querySelector('[data-testid="book-income"]')).toBeNull();
  });
});
