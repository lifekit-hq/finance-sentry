import {type Nullable} from '@lifekit-hq/ui';

import {
  type AccountBalanceItem,
  type Institution,
} from '../../../shared/models/wealth/wealth.model';
import {InstitutionNavUtils} from './institution-nav.utils';

function account(accountType: string, balanceInBaseCurrency: Nullable<number>): AccountBalanceItem {
  return {
    accountId: `${accountType}-${balanceInBaseCurrency}`,
    bankName: 'Interactive Brokers',
    accountType,
    accountNumberLast4: 'AAPL',
    currency: 'USD',
    provider: 'ibkr',
    category: 'brokerage',
    currentBalance: 1,
    balanceInBaseCurrency,
    syncStatus: 'synced',
    lastSyncTimestamp: null,
  };
}

function institution(accounts: AccountBalanceItem[]): Institution {
  return {
    institutionId: 'ibkr-1',
    provider: 'ibkr',
    name: 'Interactive Brokers',
    category: 'brokerage',
    totalInBaseCurrency: 0,
    syncStatus: 'synced',
    lastSyncTimestamp: null,
    lastSuccessfulSyncTimestamp: null,
    accounts,
  };
}

describe('InstitutionNavUtils', () => {
  it('counts positions without cash lines', () => {
    const inst = institution([
      account('brokerage', 100),
      account('brokerage', 50),
      account('cash', 10),
    ]);

    expect(InstitutionNavUtils.positionCount(inst)).toBe(2);
  });

  it('sums the cash lines in the base currency', () => {
    const inst = institution([account('cash', 2_000), account('cash', 1_008)]);

    expect(InstitutionNavUtils.cashTotal(inst)).toBe(3_008);
  });

  it('reports no cash when there are no convertible cash lines', () => {
    expect(InstitutionNavUtils.cashTotal(institution([account('brokerage', 100)]))).toBeNull();
    expect(InstitutionNavUtils.cashTotal(institution([account('cash', null)]))).toBeNull();
  });

  it('words the row as positions and cash', () => {
    const inst = institution([account('brokerage', 100), account('cash', 3_008)]);

    expect(InstitutionNavUtils.sublabel(inst, 'position', 'USD')).toBe(
      '1 position · $3,008.00 cash'
    );
  });

  it('leaves the cash part out and pluralises the noun', () => {
    const inst = institution([account('brokerage', 1), account('brokerage', 2)]);

    expect(InstitutionNavUtils.sublabel(inst, 'asset', 'USD')).toBe('2 assets');
  });

  it('words a bank by its accounts, or by its cards when it has them', () => {
    const bank = {
      ...institution([account('current', 1), account('savings', 2)]),
      category: 'banking' as const,
    };

    expect(InstitutionNavUtils.sublabel(bank, 'account', 'USD')).toBe('2 accounts');
    expect(
      InstitutionNavUtils.sublabel({...bank, accounts: [account('current', 1)]}, 'account', 'USD')
    ).toBe('1 account');
    expect(
      InstitutionNavUtils.sublabel(
        {
          ...bank,
          cards: [
            {
              cardType: 'black',
              displayName: 'Black',
              totalInBaseCurrency: 1,
              syncStatus: 'synced',
              accounts: [],
            },
          ],
        },
        'account',
        'USD'
      )
    ).toBe('1 card');
  });

  it('does not mention cash on a bank', () => {
    const bank = {...institution([account('cash', 5)]), category: 'banking' as const};

    expect(InstitutionNavUtils.sublabel(bank, 'account', 'USD')).toBe('1 account');
  });
});
