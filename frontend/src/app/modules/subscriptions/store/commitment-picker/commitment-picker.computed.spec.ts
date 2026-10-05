import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {commitmentPickerComputed} from './commitment-picker.computed';

function compute(status: AsyncStatus, errorCode: Nullable<string> = null) {
  return TestBed.runInInjectionContext(() =>
    commitmentPickerComputed({status: signal(status), errorCode: signal(errorCode)})
  );
}

describe('commitmentPickerComputed', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY}],
    });
  });

  it('isLoading follows the status', () => {
    expect(compute('loading').isLoading()).toBe(true);
    expect(compute('idle').isLoading()).toBe(false);
  });

  it('errorMessage is empty unless the load failed', () => {
    expect(compute('idle', 'INVALID_SEARCH').errorMessage()).toBe('');
  });

  it('errorMessage resolves a registered code', () => {
    expect(compute('error', 'INVALID_SEARCH').errorMessage()).toBe(
      ERROR_MESSAGES_REGISTRY['INVALID_SEARCH']
    );
  });

  it('errorMessage falls back for an unknown code', () => {
    expect(compute('error', null).errorMessage()).toBe('Failed to load transactions.');
  });
});
