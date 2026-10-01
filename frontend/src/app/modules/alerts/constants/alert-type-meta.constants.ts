import {type LucideIconName} from '@lifekit-hq/ui';

import {type AlertType} from '../models/alert/alert.model';

export interface AlertTypeMeta {
  icon: LucideIconName;
  label: string;
}

export const ALERT_TYPE_META_REGISTRY = {
  ['LowBalance']: {
    icon: 'TriangleAlert',
    label: 'low balance',
  },
  ['SyncFailure']: {
    icon: 'CircleAlert',
    label: 'sync error',
  },
  ['UnusualSpend']: {
    icon: 'Zap',
    label: 'unusual spend',
  },
  ['PolicyViolation']: {
    icon: 'ShieldAlert',
    label: 'policy violation',
  },
  ['Opportunity']: {
    icon: 'Lightbulb',
    label: 'opportunity',
  },
} satisfies Record<AlertType, AlertTypeMeta>;

/**
 * Fallback for alert types the backend may add before the frontend registry catches up —
 * keeps the alerts page from crashing on an unmapped type.
 */
export const DEFAULT_ALERT_TYPE_META: AlertTypeMeta = {
  icon: 'Bell',
  label: 'alert',
};
