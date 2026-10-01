import {type Routes} from '@angular/router';

import {BankSyncRoute} from './enums/bank-sync-route/bank-sync-route.enum';
import {provideConnectStrategies} from './strategies/provide-connect-strategies';

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
        children: [
          {path: '', redirectTo: BankSyncRoute.List, pathMatch: 'full'},
          {
            path: BankSyncRoute.List,
            loadComponent: () =>
              import('./pages/accounts-list/accounts-list.component').then(
                m => m.AccountsListComponent
              ),
          },
          {
            path: BankSyncRoute.Investments,
            loadComponent: () =>
              import('../holdings/pages/holdings/holdings.component').then(
                m => m.InvestmentsComponent
              ),
          },
        ],
      },
    ],
  },
];
