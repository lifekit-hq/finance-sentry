import {describe, expect, it} from 'vitest';

import {type ThesisInvalidationTrigger} from '../models/dossier/dossier.model';
import {ThesisTriggerUtils} from './thesis-trigger.utils';

function trigger(overrides: Partial<ThesisInvalidationTrigger> = {}): ThesisInvalidationTrigger {
  return {
    metric: 'gross_margin',
    direction: 'lessThan',
    threshold: 0.35,
    proxyTicker: null,
    consecutivePeriods: 2,
    periodType: 'quarterly',
    ...overrides,
  };
}

describe('ThesisTriggerUtils.describe', () => {
  it('turns a ratio metric into a percent sentence', () => {
    expect(ThesisTriggerUtils.describe(trigger())).toBe('Gross margin falls below 35.0%');
  });

  it('uses the "rises above" phrase for greaterThan', () => {
    expect(
      ThesisTriggerUtils.describe(
        trigger({metric: 'price_drawdown', direction: 'greaterThan', threshold: 0.45})
      )
    ).toBe('Price drawdown rises above 45.0%');
  });

  it('formats absolute metrics as money', () => {
    expect(ThesisTriggerUtils.describe(trigger({metric: 'diluted_eps', threshold: 2.5}))).toBe(
      'Diluted EPS falls below $2.50'
    );
  });

  it('appends the proxy ticker', () => {
    expect(ThesisTriggerUtils.describe(trigger({proxyTicker: 'SOXX'}))).toBe(
      'Gross margin falls below 35.0% (via SOXX)'
    );
  });

  it('humanizes snake_case in an unknown metric', () => {
    expect(ThesisTriggerUtils.describe(trigger({metric: 'services_revenue_growth'}))).toBe(
      'Services revenue growth falls below 0.35'
    );
  });

  it('spells an unknown metric as words and keeps an unknown direction as-is', () => {
    expect(
      ThesisTriggerUtils.describe(trigger({metric: 'mystery', direction: 'sideways', threshold: 7}))
    ).toBe('Mystery sideways 7');
  });
});
