export enum AppRoute {
  Root = '/',
  Login = 'login',
  AcceptInvite = 'accept-invite',
  McpConnect = 'mcp/connect',
  Accounts = 'accounts',
  AccountsList = 'accounts/list',
  AccountsInvestments = 'accounts/investments',
  Dashboard = 'dashboard',
  FlowBreakdown = 'dashboard/breakdown',
  Transactions = 'transactions',
  Income = 'income',
  Investments = 'investments',
  Budgets = 'budgets',
  Subscriptions = 'subscriptions',
  Alerts = 'alerts',
  Events = 'events',
  Ledger = 'ledger',
  Settings = 'settings',
  SettingsPeople = 'settings/people',
  AssetDossier = 'assets',
}

export const ASSET_DOSSIER_SYMBOL_PARAM = 'symbol';

/** Query parameters of the one-time invite link (see `AppRoute.AcceptInvite`). */
export const ACCEPT_INVITE_USER_PARAM = 'user';
export const ACCEPT_INVITE_TOKEN_PARAM = 'token';
