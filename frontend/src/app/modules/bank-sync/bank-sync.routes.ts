import {type Routes} from '@angular/router';
import type {PageChromeData} from '@lifekit-hq/ui';

import {RouteTitleUtils} from '../../core/title/route-title.utils';
import {ACCOUNTS_CONNECT_ACTION} from '../../shared/constants/page-actions/page-actions.constants';
import {TAB_PRELOAD_DATA} from '../../shared/constants/tab-preload/tab-preload.constants';
import {BankSyncRoute} from './enums/bank-sync-route/bank-sync-route.enum';
import {provideConnectStrategies} from './strategies/provide-connect-strategies';

// Both tabs of the accounts page share one title and the Connect action; the shell handles it.
const ACCOUNTS_TITLE = RouteTitleUtils.of('Accounts');
const ACCOUNTS_CHROME = {
  actions: [{id: ACCOUNTS_CONNECT_ACTION, label: 'Connect account', icon: 'Plus'}],
} satisfies PageChromeData;

export const BANK_SYNC_ROUTES: Routes = [
  {
    path: '',
    providers: [provideConnectStrategies()],
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./pages/accounts-shell/accounts-shell.component').then(
            m => m.AccountsShellComponent
          ),
        // Accounts is a bottom tab: its shell and default list preload with the routes file.
        data: TAB_PRELOAD_DATA,
        children: [
          {path: '', redirectTo: BankSyncRoute.List, pathMatch: 'full'},
          {
            path: BankSyncRoute.List,
            loadComponent: () =>
              import('./pages/accounts-list/accounts-list.component').then(
                m => m.AccountsListComponent
              ),
            ...ACCOUNTS_TITLE,
            data: {...ACCOUNTS_CHROME, ...TAB_PRELOAD_DATA},
          },
          {
            path: BankSyncRoute.Investments,
            loadComponent: () =>
              import('../holdings/pages/holdings/holdings.component').then(
                m => m.InvestmentsComponent
              ),
            ...ACCOUNTS_TITLE,
            data: ACCOUNTS_CHROME,
          },
        ],
      },
    ],
  },
];
