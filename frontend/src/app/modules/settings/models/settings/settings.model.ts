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
}
