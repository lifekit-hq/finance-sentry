import {describe, expect, it} from 'vitest';

import {ErrorUtils} from './error.utils';

describe('ErrorUtils.extractMessage', () => {
  it('returns the server message when nested under error', () => {
    expect(ErrorUtils.extractMessage({error: {error: 'Try again in 3 minutes.'}})).toBe(
      'Try again in 3 minutes.'
    );
  });

  it('returns null when the body or message is missing', () => {
    expect(ErrorUtils.extractMessage(null)).toBeNull();
    expect(ErrorUtils.extractMessage({error: {}})).toBeNull();
    expect(ErrorUtils.extractMessage('boom')).toBeNull();
  });
});
