import {describe, expect, it} from 'vitest';

import {AccountClosureUtils} from './account-closure.utils';

describe('AccountClosureUtils.confirms', () => {
  it('accepts the exact email', () => {
    expect(AccountClosureUtils.confirms('ann@example.com', 'ann@example.com')).toBe(true);
  });

  it('ignores case and outer spaces', () => {
    expect(AccountClosureUtils.confirms('  Ann@Example.COM ', 'ann@example.com')).toBe(true);
  });

  it('rejects a different email', () => {
    expect(AccountClosureUtils.confirms('bob@example.com', 'ann@example.com')).toBe(false);
  });

  it('rejects empty or missing text', () => {
    expect(AccountClosureUtils.confirms('', 'ann@example.com')).toBe(false);
    expect(AccountClosureUtils.confirms(null, 'ann@example.com')).toBe(false);
  });

  it('rejects anything when the email is unknown', () => {
    expect(AccountClosureUtils.confirms('', '')).toBe(false);
    expect(AccountClosureUtils.confirms(null, null)).toBe(false);
  });
});
