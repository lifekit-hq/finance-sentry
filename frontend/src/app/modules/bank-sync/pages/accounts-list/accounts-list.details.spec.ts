import {TestBed} from '@angular/core/testing';
import {CmnDialogService} from '@lifekit-hq/ui';
import {of} from 'rxjs';

import {type ClosedAccountRow} from '../../models/account-lifecycle/account-lifecycle.model';
import {type BankAccount} from '../../models/bank-account/bank-account.model';
import {AccountsStore} from '../../store/accounts/accounts.store';
import {ConnectStore} from '../../store/connect/connect.store';
import {baseInstitution, setupFixture} from './accounts-list.component.fixtures';

const closedRow: ClosedAccountRow = {
  accountId: 'closed-1',
  bankName: 'Monobank',
  kindLabel: 'Black · UAH',
  last4: '9312',
  closedLabel: 'Closed Mon 12 Oct 2026',
  reasonLabel: 'No longer listed by Monobank',
  lastBalance: 1250.5,
  currency: 'UAH',
  reopenable: true,
};

const closedAccount: BankAccount = {
  accountId: 'closed-1',
  bankName: 'Monobank',
  accountType: 'black',
  accountNumberLast4: '9312',
  currency: 'UAH',
  provider: 'monobank',
  ownerName: 'Test Owner',
  currentBalance: 1250.5,
  availableBalance: 1250.5,
  syncStatus: 'active',
  lastSyncTimestamp: null,
  lastSyncDurationMs: 640,
  createdAt: '2026-01-01T00:00:00Z',
  closedAt: '2026-10-12T12:00:00Z',
  closedReason: 'absent_from_list',
};

function setup(provider = 'ibkr', accounts: BankAccount[] = []) {
  const connectStore = {openModal: vi.fn(), selectPickedProvider: vi.fn()};
  const dialog = {open: vi.fn().mockReturnValue({afterClosed: () => of(true)})};
  const fixture = setupFixture(
    {provider, name: 'Test Bank'},
    [
      {provide: ConnectStore, useValue: connectStore},
      {provide: CmnDialogService, useValue: dialog},
    ],
    {closed: [closedRow], accounts}
  );
  const store = TestBed.inject(AccountsStore) as unknown as Record<
    string,
    ReturnType<typeof vi.fn>
  >;
  return {cmp: fixture.componentInstance, connectStore, dialog, store};
}

describe('AccountsListComponent connection details', () => {
  it.each([
    ['truelayer', ['Connection details', 'Reconnect', 'Disconnect']],
    ['ibkr', ['Connection details', 'Disconnect']],
  ])('the %s institution menu offers %j', (provider, labels) => {
    const {cmp} = setup(provider);

    expect(cmp.institutionMenu({...baseInstitution, provider}).map(item => item.label)).toEqual(
      labels
    );
  });

  it("opens the institution's details as a sheet without reconnecting or disconnecting", () => {
    const {cmp, connectStore, dialog, store} = setup();

    cmp.onInstitutionAction({...baseInstitution, provider: 'ibkr', name: 'IBKR'}, 'details');

    expect(dialog.open).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        title: 'IBKR',
        phone: 'action-sheet',
        data: {
          rows: expect.arrayContaining([
            {label: 'Provider', value: 'Interactive Brokers'},
            {label: 'Status', value: 'Failed'},
          ]),
        },
      })
    );
    expect(connectStore.openModal).not.toHaveBeenCalled();
    expect(store['disconnectAccount']).not.toHaveBeenCalled();
  });

  it("opens a closed account's details as a read-only sheet", () => {
    const {cmp, dialog, store} = setup('ibkr', [closedAccount]);

    cmp.onClosedAccountAction(closedRow, 'details');

    expect(dialog.open).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        title: 'Monobank •••• 9312',
        phone: 'action-sheet',
        data: {
          rows: expect.arrayContaining([
            {label: 'Provider', value: 'Monobank'},
            {label: 'Reason', value: 'No longer listed by Monobank'},
            {label: 'Sync took', value: '640 ms'},
          ]),
        },
      })
    );
    expect(store['reopenAccount']).not.toHaveBeenCalled();
    expect(store['disconnectAccount']).not.toHaveBeenCalled();
  });

  it('opens nothing for a closed row whose account is not in the loaded list', () => {
    const {cmp, dialog} = setup();

    cmp.onClosedAccountAction(closedRow, 'details');

    expect(dialog.open).not.toHaveBeenCalled();
  });
});
