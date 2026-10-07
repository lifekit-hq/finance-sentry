import {describe, expect, it} from 'vitest';

import {
  ALERT_TYPE_META_REGISTRY,
  DEFAULT_ALERT_TYPE_META,
} from '../constants/alert-type-meta.constants';
import {type AlertType} from '../models/alert/alert.model';
import {AlertTypeUtils} from './alert-type.utils';

describe('AlertTypeUtils.meta', () => {
  it('returns the registered meta for a known type', () => {
    expect(AlertTypeUtils.meta('NewsCluster')).toEqual(ALERT_TYPE_META_REGISTRY.NewsCluster);
  });

  it('covers every backend alert type with its own icon and label', () => {
    const types = Object.keys(ALERT_TYPE_META_REGISTRY);
    expect(types).toHaveLength(27);
    for (const type of types) {
      expect(AlertTypeUtils.meta(type as AlertType)).not.toBe(DEFAULT_ALERT_TYPE_META);
    }
    expect(new Set(types.map(t => AlertTypeUtils.meta(t as AlertType).label)).size).toBe(27);
  });

  it('falls back for a type the registry does not know yet', () => {
    expect(AlertTypeUtils.meta('SomethingNew' as AlertType)).toBe(DEFAULT_ALERT_TYPE_META);
  });
});
