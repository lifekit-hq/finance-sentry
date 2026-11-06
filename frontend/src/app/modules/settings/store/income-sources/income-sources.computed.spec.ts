import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus, ERROR_MESSAGES} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {beforeEach, describe, expect, it} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {type IncomeSource} from '../../models/income-source/income-source.model';
import {incomeSourcesComputed} from './income-sources.computed';
import {MONTHLY_SALARY, RETIRED_SIDE} from './income-sources.fixtures';

function build(
  overrides: Partial<{
    sources: IncomeSource[];
    sourcesStatus: AsyncStatus;
    actionErrorCode: Nullable<string>;
  }> = {}
) {
  return {
    sources: signal<IncomeSource[]>(overrides.sources ?? []),
    sourcesStatus: signal<AsyncStatus>(overrides.sourcesStatus ?? 'idle'),
    actionErrorCode: signal<Nullable<string>>(overrides.actionErrorCode ?? null),
  };
}

function compute(store: ReturnType<typeof build>) {
  return TestBed.runInInjectionContext(() => incomeSourcesComputed(store));
}

describe('incomeSourcesComputed', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY}],
    });
  });

  it('is loading only while nothing is on screen', () => {
    expect(compute(build({sourcesStatus: 'loading'})).isLoading()).toBe(true);
    expect(compute(build({sourcesStatus: 'loading', sources: [MONTHLY_SALARY]})).isLoading()).toBe(
      false
    );
  });

  it('describes a running source by kind, cadence and basis', () => {
    const [row] = compute(build({sources: [MONTHLY_SALARY]})).rows();

    expect(row.sublabel).toBe('Salary · Monthly · After tax');
  });

  it('adds the end date to a retired source', () => {
    const [row] = compute(build({sources: [RETIRED_SIDE]})).rows();

    expect(row.sublabel).toBe('Side income · Irregular · Before tax · ended Tue 30 Jun 2026');
  });

  it('has no action error message without a code', () => {
    expect(compute(build()).actionErrorMessage()).toBe('');
  });

  it('resolves a registered code to its message', () => {
    const {actionErrorMessage} = compute(build({actionErrorCode: 'INCOME_SOURCE_NOT_FOUND'}));

    expect(actionErrorMessage()).toBe(ERROR_MESSAGES_REGISTRY['INCOME_SOURCE_NOT_FOUND']);
  });

  it('falls back for an unregistered code', () => {
    const {actionErrorMessage} = compute(build({actionErrorCode: 'SOMETHING_ELSE'}));

    expect(actionErrorMessage()).toBe('Could not update the income source.');
  });
});
