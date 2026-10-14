import {TestBed} from '@angular/core/testing';
import {describe, expect, it} from 'vitest';

import {IbkrFlexGuideComponent} from './ibkr-flex-guide.component';
import {
  FLEX_CASH_TRANSACTION_DEPOSITS_OPTION,
  FLEX_RECOMMENDED_SECTIONS,
} from './ibkr-flex-guide.constants';

describe('IbkrFlexGuideComponent', () => {
  const render = (): string => {
    const fixture = TestBed.createComponent(IbkrFlexGuideComponent);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  };

  it('names the Deposits & Withdrawals option under Cash Transactions', () => {
    expect(FLEX_CASH_TRANSACTION_DEPOSITS_OPTION).toBe('Deposits & Withdrawals');
    expect(render()).toContain('Deposits & Withdrawals');
  });

  it('recommends Change in NAV', () => {
    expect(FLEX_RECOMMENDED_SECTIONS).toContain('Change in NAV');
    expect(render()).toContain('Change in NAV');
  });
});
