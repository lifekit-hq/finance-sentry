import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {ACCOUNTS_COLLAPSED_STORAGE_KEY} from '../constants/accounts-disclosure/accounts-disclosure.constants';
import {AccountsDisclosureUtils} from './accounts-disclosure.utils';

describe('AccountsDisclosureUtils', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('keys an institution by provider and id', () => {
    expect(AccountsDisclosureUtils.key({provider: 'truelayer', institutionId: 'aib'})).toBe(
      'truelayer:aib'
    );
  });

  it('reads back what it wrote', () => {
    AccountsDisclosureUtils.write(['truelayer:aib']);

    expect(AccountsDisclosureUtils.read()).toEqual(['truelayer:aib']);
  });

  it('reads empty when nothing is stored', () => {
    expect(AccountsDisclosureUtils.read()).toEqual([]);
  });

  it('reads empty from a malformed value', () => {
    localStorage.setItem(ACCOUNTS_COLLAPSED_STORAGE_KEY, '{not json');
    expect(AccountsDisclosureUtils.read()).toEqual([]);

    localStorage.setItem(ACCOUNTS_COLLAPSED_STORAGE_KEY, '{"a":1}');
    expect(AccountsDisclosureUtils.read()).toEqual([]);
  });

  it('drops non-string entries', () => {
    localStorage.setItem(ACCOUNTS_COLLAPSED_STORAGE_KEY, '["truelayer:aib", 3, null]');

    expect(AccountsDisclosureUtils.read()).toEqual(['truelayer:aib']);
  });

  it('survives blocked storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(AccountsDisclosureUtils.read()).toEqual([]);
    expect(() => AccountsDisclosureUtils.write(['x'])).not.toThrow();
  });
});
