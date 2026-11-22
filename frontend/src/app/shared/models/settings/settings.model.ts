export type BaseCurrency = 'USD' | 'EUR' | 'GBP' | 'UAH' | 'BTC';

export interface UserProfile {
  firstName: string;
  lastName: string;
  email: string;
  baseCurrency: BaseCurrency;
  emailAlerts: boolean;
  lowBalanceAlerts: boolean;
  lowBalanceThreshold: number;
  syncFailureAlerts: boolean;
  watchlistAnalystAlerts: boolean;
  safeWithdrawalRate: number;
  realAnnualReturn: number;
  twoFactor: boolean;
  hideEmptyBalances: boolean;
  hideSmallBalances: boolean;
  /** In the base currency; the server accepts above 0 up to `SMALL_BALANCE_THRESHOLD_MAX`. */
  smallBalanceThreshold: number;
}

export interface UpdateProfileRequest {
  firstName: string;
  lastName: string;
  baseCurrency: BaseCurrency;
  emailAlerts: boolean;
  lowBalanceAlerts: boolean;
  lowBalanceThreshold: number;
  syncFailureAlerts: boolean;
  watchlistAnalystAlerts: boolean;
  safeWithdrawalRate: number;
  realAnnualReturn: number;
  hideEmptyBalances: boolean;
  hideSmallBalances: boolean;
  smallBalanceThreshold: number;
}
