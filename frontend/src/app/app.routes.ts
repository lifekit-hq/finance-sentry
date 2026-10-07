import {inject} from '@angular/core';
import {type ActivatedRouteSnapshot, Router, type Routes} from '@angular/router';
import type {PageChromeData} from '@lifekit-hq/ui';

import {authGuard} from './modules/auth/guards/auth.guard';
import {guestGuard} from './modules/auth/guards/guest.guard';
import {permissionGuard} from './modules/auth/guards/permission.guard';
import {
  ALERTS_MARK_ALL_READ_ACTION,
  BUDGETS_ADD_ACTION,
  SUBSCRIPTIONS_ADD_ACTION,
} from './shared/constants/page-actions/page-actions.constants';
import {AppRoute, ASSET_DOSSIER_SYMBOL_PARAM} from './shared/enums/app-route/app-route.enum';
import {Permission} from './shared/enums/permission/permission.enum';

export const APP_ROUTES: Routes = [
  {
    path: AppRoute.Login.slice(1),
    loadComponent: () =>
      import('./modules/auth/pages/login/login.component').then(m => m.LoginComponent),
    canActivate: [guestGuard],
  },
  {
    path: AppRoute.AcceptInvite.slice(1),
    loadComponent: () =>
      import('./modules/auth/pages/accept-invite/accept-invite.component').then(
        m => m.AcceptInviteComponent
      ),
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
        data: {parent: AppRoute.Dashboard} satisfies PageChromeData,
        // A dashboard window (`from`/`to`) and a calendar month are different breakdowns.
        runGuardsAndResolvers: 'paramsOrQueryParamsChange',
        resolve: {
          title: (route: ActivatedRouteSnapshot) =>
            route.queryParamMap.has('from') || route.queryParamMap.has('to')
              ? 'Window breakdown'
              : 'Month breakdown',
        },
      },
      {
        path: AppRoute.More.slice(1),
        loadComponent: () =>
          import('./core/shell/more-page/more-page.component').then(m => m.MorePageComponent),
        data: {title: 'More'} satisfies PageChromeData,
      },
      {
        path: AppRoute.Dashboard.slice(1),
        loadComponent: () =>
          import('./modules/bank-sync/pages/dashboard/dashboard.component').then(
            m => m.DashboardComponent
          ),
        data: {title: 'Dashboard'} satisfies PageChromeData,
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
        data: {title: 'Transactions'} satisfies PageChromeData,
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
        data: {
          title: 'Budgets',
          actions: [{id: BUDGETS_ADD_ACTION, label: 'Add budget', icon: 'Plus'}],
        } satisfies PageChromeData,
      },
      {
        path: AppRoute.Subscriptions.slice(1),
        loadComponent: () =>
          import('./modules/subscriptions/pages/subscriptions/subscriptions.component').then(
            m => m.SubscriptionsComponent
          ),
        data: {
          title: 'Subscriptions',
          actions: [{id: SUBSCRIPTIONS_ADD_ACTION, label: 'Add subscription', icon: 'Plus'}],
        } satisfies PageChromeData,
      },
      {
        path: AppRoute.Alerts.slice(1),
        loadComponent: () =>
          import('./modules/alerts/pages/alerts/alerts.component').then(m => m.AlertsComponent),
        data: {
          title: 'Alerts',
          actions: [{id: ALERTS_MARK_ALL_READ_ACTION, label: 'Mark all read', icon: 'CheckCheck'}],
        } satisfies PageChromeData,
      },
      {
        path: AppRoute.Events.slice(1),
        loadComponent: () =>
          import('./modules/events/pages/events/events.component').then(m => m.EventsComponent),
        data: {title: 'Events'} satisfies PageChromeData,
      },
      {
        path: AppRoute.Ledger.slice(1),
        canMatch: [permissionGuard(Permission.AiUse)],
        loadComponent: () =>
          import('./modules/agent/pages/ledger-chat/ledger-chat.component').then(
            m => m.LedgerChatComponent
          ),
        data: {title: 'Ledger'} satisfies PageChromeData,
      },
      {
        path: AppRoute.SettingsPeople.slice(1),
        canMatch: [permissionGuard(Permission.UsersManage)],
        loadComponent: () =>
          import('./modules/settings/pages/people/people.component').then(m => m.PeopleComponent),
        data: {title: 'People', parent: AppRoute.Settings} satisfies PageChromeData,
      },
      {
        path: AppRoute.Settings.slice(1),
        loadComponent: () =>
          import('./modules/settings/pages/settings/settings.component').then(
            m => m.SettingsComponent
          ),
        data: {title: 'Settings'} satisfies PageChromeData,
      },
      {
        path: `${AppRoute.AssetDossier.slice(1)}/:${ASSET_DOSSIER_SYMBOL_PARAM}`,
        loadComponent: () =>
          import('./modules/assets/pages/asset-dossier/asset-dossier.component').then(
            m => m.AssetDossierComponent
          ),
        // The top bar's title is the symbol; the path names it, so a resolver lifts it into the data.
        data: {parent: AppRoute.AccountsInvestments} satisfies PageChromeData,
        resolve: {
          title: (route: ActivatedRouteSnapshot) =>
            route.paramMap.get(ASSET_DOSSIER_SYMBOL_PARAM)?.toUpperCase() ?? 'Asset',
        },
      },
      {path: '', redirectTo: AppRoute.Dashboard, pathMatch: 'full'},
    ],
  },
];
