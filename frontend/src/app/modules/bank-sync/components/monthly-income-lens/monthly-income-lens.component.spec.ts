import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {type MonthlyIncomeLens} from '../../models/income-lens/income-lens.model';
import {monthlyIncomeLensComputed} from '../../store/monthly-income-lens/monthly-income-lens.computed';
import {monthlyIncomeLens} from '../../store/monthly-income-lens/monthly-income-lens.fixtures';
import {MonthlyIncomeLensStore} from '../../store/monthly-income-lens/monthly-income-lens.store';
import {MonthlyIncomeLensComponent} from './monthly-income-lens.component';

function render(lens: Nullable<MonthlyIncomeLens>) {
  const computeds = monthlyIncomeLensComputed({lens: signal(lens)});
  TestBed.overrideComponent(MonthlyIncomeLensComponent, {
    set: {providers: [{provide: MonthlyIncomeLensStore, useValue: computeds}]},
  });
  const fixture = TestBed.createComponent(MonthlyIncomeLensComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('MonthlyIncomeLensComponent', () => {
  it('shows the monthly income with its assumptions in words', () => {
    const el = render(monthlyIncomeLens());
    const line = el.querySelector('[data-testid="monthly-income-lens"]')?.textContent ?? '';
    expect(line).toContain('As income: about');
    expect(line).toContain('$1,000 a month');
    const assumptions =
      el.querySelector('[data-testid="monthly-income-lens-assumptions"]')?.textContent ?? '';
    expect(assumptions).toContain('Your $300,000 net worth drawn at 4% a year');
    expect(assumptions).toContain('Hypothetical, not a guarantee.');
  });

  it('notes stale data when a source is stale', () => {
    const el = render(monthlyIncomeLens({hasStaleSleeves: true}));
    expect(el.querySelector('[data-testid="monthly-income-lens-stale"]')?.textContent).toContain(
      'this is an estimate'
    );
  });

  it('omits the stale note when no source is stale', () => {
    const el = render(monthlyIncomeLens());
    expect(el.querySelector('[data-testid="monthly-income-lens-stale"]')).toBeNull();
  });

  it('renders nothing before it loads or with no positive net worth', () => {
    expect(render(null).querySelector('[data-testid="monthly-income-lens"]')).toBeNull();
    TestBed.resetTestingModule();
    const el = render(monthlyIncomeLens({status: 'NoNetWorth', monthlyIncome: null}));
    expect(el.querySelector('[data-testid="monthly-income-lens"]')).toBeNull();
    expect(el.querySelector('[data-testid="monthly-income-lens-assumptions"]')).toBeNull();
  });
});
