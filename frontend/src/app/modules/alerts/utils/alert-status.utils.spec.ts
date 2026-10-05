import {describe, expect, it} from 'vitest';

import {AlertStatusUtils} from './alert-status.utils';

describe('AlertStatusUtils.caption', () => {
  it('is null for an open alert that fired once', () => {
    expect(AlertStatusUtils.caption({isResolved: false, occurrenceCount: 1})).toBeNull();
  });

  it('marks a resolved alert', () => {
    expect(AlertStatusUtils.caption({isResolved: true, occurrenceCount: 1})).toBe('Resolved');
  });

  it('shows how often a repeat was suppressed', () => {
    expect(AlertStatusUtils.caption({isResolved: false, occurrenceCount: 14})).toBe('×14');
  });

  it('combines both', () => {
    expect(AlertStatusUtils.caption({isResolved: true, occurrenceCount: 3})).toBe('Resolved · ×3');
  });

  it('tolerates a payload without the counter', () => {
    expect(AlertStatusUtils.caption({isResolved: false, occurrenceCount: Number.NaN})).toBeNull();
  });
});
