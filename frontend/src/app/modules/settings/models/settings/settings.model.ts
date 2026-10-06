export type BaseCurrency = 'USD' | 'EUR' | 'GBP' | 'UAH' | 'BTC';
export type ThemePreference = 'system' | 'light' | 'dark';

export interface UserProfile {
  firstName: string;
  lastName: string;
  email: string;
  baseCurrency: BaseCurrency;
  theme: ThemePreference;
  emailAlerts: boolean;
  lowBalanceAlerts: boolean;
  lowBalanceThreshold: number;
  syncFailureAlerts: boolean;
  safeWithdrawalRate: number;
  realAnnualReturn: number;
  twoFactor: boolean;
}

export interface UpdateProfileRequest {
  firstName: string;
  lastName: string;
  baseCurrency: BaseCurrency;
  theme: ThemePreference;
  emailAlerts: boolean;
  lowBalanceAlerts: boolean;
  lowBalanceThreshold: number;
  syncFailureAlerts: boolean;
  safeWithdrawalRate: number;
  realAnnualReturn: number;
}

export interface PasswordForm {
  current: string;
  next: string;
  confirm: string;
}
