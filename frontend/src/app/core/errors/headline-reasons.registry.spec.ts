import {describe, expect, it} from 'vitest';

import {HEADLINE_REASON_CODES} from '../../shared/constants/headline/headline.constants';
import {ERROR_MESSAGES_REGISTRY} from './error-messages.registry';
import {HEADLINE_REASON_MESSAGES} from './headline-reasons.registry';

describe('headline reasons registry', () => {
  it.each(HEADLINE_REASON_CODES)('has words for %s', code => {
    expect(HEADLINE_REASON_MESSAGES[code]?.length).toBeGreaterThan(0);
  });

  it('has no entry beyond the known codes', () => {
    expect(Object.keys(HEADLINE_REASON_MESSAGES).sort()).toEqual([...HEADLINE_REASON_CODES].sort());
  });

  it('is part of the app-wide message registry', () => {
    for (const code of HEADLINE_REASON_CODES) {
      expect(ERROR_MESSAGES_REGISTRY[code]).toBe(HEADLINE_REASON_MESSAGES[code]);
    }
  });
});
