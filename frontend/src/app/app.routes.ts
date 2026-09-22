import {inject} from '@angular/core';
import {Router, type Routes} from '@angular/router';

import {authGuard} from './modules/auth/guards/auth.guard';
import {guestGuard} from './modules/auth/guards/guest.guard';
import {AppRoute, ASSET_DOSSIER_SYMBOL_PARAM} from './shared/enums/app-route/app-route.enum';

export const APP_ROUTES: Routes = [
  {
    path: AppRoute.Login.slice(1),
    loadComponent: () =>
      import('./modules/auth/pages/login/login.component').then(m => m.LoginComponent),
    canActivate: [guestGuard],
  },
  {
    path: AppRoute.Register.slice(1),
    loadComponent: () =>
      import('./modules/auth/pages/register/register.component').then(m => m.RegisterComponent),
    canActivate: [guestGuard],
  },
  {
    path: AppRoute.McpConnect.slice(1),
    loadComponent: () =>
      import('./modules/auth/pages/mcp-connect/mcp-connect.component').then(
        m => m.McpConnectComponent
      ),
  },
  {
    path: '',
    loadComponent: () => import('./core/shell/app-shell.component').then(m => m.AppShellComponent),
    canActivate: [authGuard],
    children: [
      {
        path: AppRoute.FlowBreakdown.slice(1),
        loadComponent: () =>
          import('./modules/bank-sync/pages/flow-breakdown/flow-breakdown.component').then(
            m => m.FlowBreakdownComponent
          ),
      },
      {
        path: AppRoute.Dashboard.slice(1),
        loadComponent: () =>
          import('./modules/bank-sync/pages/dashboard/dashboard.component').then(
            m => m.DashboardComponent
          ),
      },
      {
        path: AppRoute.Accounts.slice(1),
        loadChildren: () =>
          import('./modules/bank-sync/bank-sync.routes').then(
            ({BANK_SYNC_ROUTES}) => BANK_SYNC_ROUTES
          ),
      },
      {
        path: AppRoute.Transactions.slice(1),
        loadComponent: () =>
          import('./modules/bank-sync/pages/transaction-ledger/transaction-ledger.component').then(
            m => m.TransactionLedgerComponent
          ),
      },
      {
        // The Income page was the transaction ledger filtered to credits, plus charts the
        // dashboard already owns. Kept as a redirect so old links and bookmarks still land
        // somewhere sensible instead of on a dead route.
        path: AppRoute.Income.slice(1),
        redirectTo: () => inject(Router).parseUrl(`${AppRoute.Transactions}?type=credit`),
      },
      {
        path: AppRoute.Investments.slice(1),
        redirectTo: AppRoute.AccountsInvestments.slice(1),
        pathMatch: 'full',
      },
      {
        path: AppRoute.Budgets.slice(1),
        loadComponent: () =>
          import('./modules/budgets/pages/budgets/budgets.component').then(m => m.BudgetsComponent),
      },
      {
        path: AppRoute.Subscriptions.slice(1),
        loadComponent: () =>
          import('./modules/subscriptions/pages/subscriptions/subscriptions.component').then(
            m => m.SubscriptionsComponent
          ),
      },
      {
        path: AppRoute.Alerts.slice(1),
        loadComponent: () =>
          import('./modules/alerts/pages/alerts/alerts.component').then(m => m.AlertsComponent),
      },
      {
        path: AppRoute.Events.slice(1),
        loadComponent: () =>
          import('./modules/events/pages/events/events.component').then(m => m.EventsComponent),
      },
      {
        path: AppRoute.Ledger.slice(1),
        loadComponent: () =>
          import('./modules/agent/pages/ledger-chat/ledger-chat.component').then(
            m => m.LedgerChatComponent
          ),
      },
      {
        path: AppRoute.Settings.slice(1),
        loadComponent: () =>
          import('./modules/settings/pages/settings/settings.component').then(
            m => m.SettingsComponent
          ),
      },
      {
        path: `${AppRoute.AssetDossier.slice(1)}/:${ASSET_DOSSIER_SYMBOL_PARAM}`,
        loadComponent: () =>
          import('./modules/assets/pages/asset-dossier/asset-dossier.component').then(
            m => m.AssetDossierComponent
          ),
      },
      {path: '', redirectTo: AppRoute.Dashboard, pathMatch: 'full'},
    ],
  },
];
