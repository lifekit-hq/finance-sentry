import {describe, expect, it} from 'vitest';

import {AlertSeverityUtils} from './alert-severity.utils';

describe('AlertSeverityUtils.toItemSeverity', () => {
  it('maps Error', () => {
    expect(AlertSeverityUtils.toItemSeverity('Error')).toBe('error');
  });

  it('maps Warning', () => {
    expect(AlertSeverityUtils.toItemSeverity('Warning')).toBe('warning');
  });

  it('maps Info', () => {
    expect(AlertSeverityUtils.toItemSeverity('Info')).toBe('info');
  });
});
