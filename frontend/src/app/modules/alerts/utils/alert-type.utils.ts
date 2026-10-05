import {
  ALERT_TYPE_META_REGISTRY,
  type AlertTypeMeta,
  DEFAULT_ALERT_TYPE_META,
} from '../constants/alert-type-meta.constants';
import {type AlertType} from '../models/alert/alert.model';

export class AlertTypeUtils {
  /**
   * Display metadata for an alert type. Tolerates types the backend added before the registry
   * did — an unmapped type must not crash the whole alerts page.
   */
  public static meta(type: AlertType): AlertTypeMeta {
    const registry = ALERT_TYPE_META_REGISTRY as Record<string, AlertTypeMeta | undefined>;
    return registry[type] ?? DEFAULT_ALERT_TYPE_META;
  }
}
