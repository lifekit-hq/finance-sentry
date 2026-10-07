import {type LucideIconName} from '@lifekit-hq/ui';

import {AppRoute} from '../../../shared/enums/app-route/app-route.enum';
import {type AlertType} from '../models/alert/alert.model';
import {type AlertDestination} from '../models/alert/alert-destination.model';

export interface AlertTypeMeta {
  icon: LucideIconName;
  label: string;
  destination: AlertDestination;
}

export const ALERT_TYPE_META_REGISTRY = {
  ['LowBalance']: {
    icon: 'TriangleAlert',
    label: 'low balance',
    destination: AppRoute.AccountsList,
  },
  ['SyncFailure']: {
    icon: 'CircleAlert',
    label: 'sync error',
    destination: AppRoute.AccountsList,
  },
  // Retired: nothing generates it any more, but alerts already stored under it still render.
  ['UnusualSpend']: {
    icon: 'Zap',
    label: 'unusual spend',
    destination: AppRoute.Transactions,
  },
  ['ThesisBroken']: {
    icon: 'HeartCrack',
    label: 'thesis broken',
    destination: 'dossier',
  },
  ['MarketStructure']: {
    icon: 'Activity',
    label: 'market move',
    destination: 'dossier',
  },
  ['PolicyViolation']: {
    icon: 'ShieldAlert',
    label: 'policy violation',
    destination: AppRoute.AccountsInvestments,
  },
  ['Opportunity']: {
    icon: 'Lightbulb',
    label: 'opportunity',
    destination: 'dossier',
  },
  ['ConsentExpiring']: {
    icon: 'Clock',
    label: 'consent expiring',
    destination: AppRoute.AccountsList,
  },
  ['JobFailure']: {
    icon: 'Wrench',
    label: 'job failure',
    destination: 'none',
  },
  ['PerformanceBrief']: {
    icon: 'ChartLine',
    label: 'performance brief',
    destination: AppRoute.AccountsInvestments,
  },
  ['CashShortfall']: {
    icon: 'Wallet',
    label: 'cash shortfall',
    destination: AppRoute.AccountsList,
  },
  ['PriceHike']: {
    icon: 'ArrowUpRight',
    label: 'price hike',
    destination: AppRoute.Subscriptions,
  },
  ['DuplicateCharge']: {
    icon: 'Copy',
    label: 'duplicate charge',
    destination: AppRoute.Transactions,
  },
  ['CategorySpike']: {
    icon: 'ChartColumn',
    label: 'spending spike',
    destination: AppRoute.Transactions,
  },
  ['FxSpread']: {
    icon: 'ArrowLeftRight',
    label: 'fx spread',
    destination: AppRoute.Transactions,
  },
  ['RebalanceProposal']: {
    icon: 'Scale',
    label: 'rebalance',
    destination: AppRoute.AccountsInvestments,
  },
  ['CashSweepProposal']: {
    icon: 'PiggyBank',
    label: 'cash sweep',
    destination: AppRoute.AccountsInvestments,
  },
  ['EarningsAhead']: {
    icon: 'CalendarClock',
    label: 'event ahead',
    destination: 'dossier',
  },
  ['FilingLanded']: {
    icon: 'FileText',
    label: 'filing',
    destination: 'dossier',
  },
  ['AnalystRatingChange']: {
    icon: 'TrendingUp',
    label: 'analyst rating',
    destination: 'dossier',
  },
  ['NewsCluster']: {
    icon: 'Newspaper',
    label: 'news',
    destination: 'dossier',
  },
  ['BudgetBreach']: {
    icon: 'Gauge',
    label: 'budget',
    destination: AppRoute.Budgets,
  },
  ['FamilyStatement']: {
    icon: 'Users',
    label: 'family statement',
    destination: AppRoute.AccountsList,
  },
  ['FireBrief']: {
    icon: 'Flame',
    label: 'FIRE brief',
    destination: AppRoute.Dashboard,
  },
  ['PolicyReview']: {
    icon: 'ClipboardCheck',
    label: 'policy review',
    destination: AppRoute.AccountsInvestments,
  },
  ['PolicyReviewMissed']: {
    icon: 'ClipboardX',
    label: 'review missed',
    destination: AppRoute.AccountsInvestments,
  },
  ['RelativeUnderperformance']: {
    icon: 'TrendingDown',
    label: 'underperformance',
    destination: AppRoute.AccountsInvestments,
  },
} satisfies Record<AlertType, AlertTypeMeta>;

/**
 * Fallback for alert types the backend may add before the frontend registry catches up —
 * keeps the alerts page from crashing on an unmapped type.
 */
export const DEFAULT_ALERT_TYPE_META: AlertTypeMeta = {
  icon: 'Bell',
  label: 'alert',
  destination: 'none',
};
