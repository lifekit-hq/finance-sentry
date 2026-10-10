import {inject} from '@angular/core';
import {type ActivatedRouteSnapshot, Router, type Routes} from '@angular/router';
import type {PageChromeData} from '@lifekit-hq/ui';

import {RouteTitleUtils} from './core/title/route-title.utils';
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
    ...RouteTitleUtils.of('Sign in'),
  },
  {
    path: AppRoute.AcceptInvite.slice(1),
    loadComponent: () =>
      import('./modules/auth/pages/accept-invite/accept-invite.component').then(
        m => m.AcceptInviteComponent
      ),
    canActivate: [guestGuard],
    ...RouteTitleUtils.of('Accept invitation'),
  },
  {
    path: AppRoute.McpConnect.slice(1),
    loadComponent: () =>
      import('./modules/auth/pages/mcp-connect/mcp-connect.component').then(
        m => m.McpConnectComponent
      ),
    ...RouteTitleUtils.of('Connect MCP'),
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
        ...RouteTitleUtils.of((route: ActivatedRouteSnapshot) =>
          route.queryParamMap.has('from') || route.queryParamMap.has('to')
            ? 'Window breakdown'
            : 'Month breakdown'
        ),
      },
      {
        path: AppRoute.More.slice(1),
        loadComponent: () =>
          import('./core/shell/more-page/more-page.component').then(m => m.MorePageComponent),
        ...RouteTitleUtils.of('More'),
      },
      {
        path: AppRoute.Dashboard.slice(1),
        loadComponent: () =>
          import('./modules/bank-sync/pages/dashboard/dashboard.component').then(
            m => m.DashboardComponent
          ),
        ...RouteTitleUtils.of('Dashboard'),
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
        ...RouteTitleUtils.of('Transactions'),
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
        ...RouteTitleUtils.of('Budgets'),
        data: {
          actions: [{id: BUDGETS_ADD_ACTION, label: 'Add budget', icon: 'Plus'}],
        } satisfies PageChromeData,
      },
      {
        path: AppRoute.Subscriptions.slice(1),
        loadComponent: () =>
          import('./modules/subscriptions/pages/subscriptions/subscriptions.component').then(
            m => m.SubscriptionsComponent
          ),
        ...RouteTitleUtils.of('Subscriptions'),
        data: {
          actions: [{id: SUBSCRIPTIONS_ADD_ACTION, label: 'Add subscription', icon: 'Plus'}],
        } satisfies PageChromeData,
      },
      {
        path: AppRoute.Alerts.slice(1),
        loadComponent: () =>
          import('./modules/alerts/pages/alerts/alerts.component').then(m => m.AlertsComponent),
        ...RouteTitleUtils.of('Alerts'),
        data: {
          actions: [{id: ALERTS_MARK_ALL_READ_ACTION, label: 'Mark all read', icon: 'CheckCheck'}],
        } satisfies PageChromeData,
      },
      {
        path: AppRoute.Events.slice(1),
        loadComponent: () =>
          import('./modules/events/pages/events/events.component').then(m => m.EventsComponent),
        ...RouteTitleUtils.of('Events'),
      },
      {
        path: AppRoute.Ledger.slice(1),
        canMatch: [permissionGuard(Permission.AiUse)],
        loadComponent: () =>
          import('./modules/agent/pages/ledger-chat/ledger-chat.component').then(
            m => m.LedgerChatComponent
          ),
        ...RouteTitleUtils.of('Ledger'),
      },
      {
        path: AppRoute.SettingsPeople.slice(1),
        canMatch: [permissionGuard(Permission.UsersManage)],
        loadComponent: () =>
          import('./modules/settings/pages/people/people.component').then(m => m.PeopleComponent),
        ...RouteTitleUtils.of('People'),
        data: {parent: AppRoute.Settings} satisfies PageChromeData,
      },
      {
        path: AppRoute.Settings.slice(1),
        loadComponent: () =>
          import('./modules/settings/pages/settings/settings.component').then(
            m => m.SettingsComponent
          ),
        ...RouteTitleUtils.of('Settings'),
      },
      {
        path: `${AppRoute.AssetDossier.slice(1)}/:${ASSET_DOSSIER_SYMBOL_PARAM}`,
        loadComponent: () =>
          import('./modules/assets/pages/asset-dossier/asset-dossier.component').then(
            m => m.AssetDossierComponent
          ),
        // The title is the symbol; the path names it, so the title resolves from the param.
        data: {parent: AppRoute.AccountsInvestments} satisfies PageChromeData,
        ...RouteTitleUtils.of(
          (route: ActivatedRouteSnapshot) =>
            route.paramMap.get(ASSET_DOSSIER_SYMBOL_PARAM)?.toUpperCase() ?? 'Asset'
        ),
      },
      {path: '', redirectTo: AppRoute.Dashboard, pathMatch: 'full'},
    ],
  },
];
