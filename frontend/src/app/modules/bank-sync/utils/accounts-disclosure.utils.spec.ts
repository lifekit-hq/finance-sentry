import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {
  ACCOUNTS_COLLAPSED_STORAGE_KEY,
  ACCOUNTS_OPEN_STORAGE_KEY,
} from '../constants/accounts-disclosure/accounts-disclosure.constants';
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

  it('opens a bank and closes a broker or exchange by default', () => {
    expect(AccountsDisclosureUtils.defaultOpen('banking')).toBe(true);
    expect(AccountsDisclosureUtils.defaultOpen('brokerage')).toBe(false);
    expect(AccountsDisclosureUtils.defaultOpen('crypto')).toBe(false);
  });

  it('reads back what it wrote', () => {
    AccountsDisclosureUtils.write({'truelayer:aib': false, 'ibkr:ibkr-1': true});

    expect(AccountsDisclosureUtils.read()).toEqual({'truelayer:aib': false, 'ibkr:ibkr-1': true});
  });

  it('reads empty when nothing is stored', () => {
    expect(AccountsDisclosureUtils.read()).toEqual({});
  });

  it('reads empty from a malformed value', () => {
    localStorage.setItem(ACCOUNTS_OPEN_STORAGE_KEY, '{not json');
    expect(AccountsDisclosureUtils.read()).toEqual({});

    localStorage.setItem(ACCOUNTS_OPEN_STORAGE_KEY, '["a"]');
    expect(AccountsDisclosureUtils.read()).toEqual({});
  });

  it('drops entries that are not booleans', () => {
    localStorage.setItem(ACCOUNTS_OPEN_STORAGE_KEY, '{"a":true,"b":"yes","c":null}');

    expect(AccountsDisclosureUtils.read()).toEqual({a: true});
  });

  it('folds the earlier list of collapsed groups in as closed, the new record winning', () => {
    localStorage.setItem(ACCOUNTS_COLLAPSED_STORAGE_KEY, '["truelayer:aib", 3, "truelayer:boi"]');
    localStorage.setItem(ACCOUNTS_OPEN_STORAGE_KEY, '{"truelayer:boi":true}');

    expect(AccountsDisclosureUtils.read()).toEqual({
      'truelayer:aib': false,
      'truelayer:boi': true,
    });
  });

  it('survives blocked storage', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(AccountsDisclosureUtils.read()).toEqual({});
    expect(() => AccountsDisclosureUtils.write({x: true})).not.toThrow();
  });
});
