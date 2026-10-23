import {describe, expect, it} from 'vitest';

import {OutflowUtils} from './outflow.utils';

describe('OutflowUtils', () => {
  describe('sameScope', () => {
    const scope = {accountId: 'a', from: '2026-10-01', to: '2026-10-31'};

    it('matches an identical scope', () => {
      expect(OutflowUtils.sameScope(scope, {...scope})).toBe(true);
    });

    it.each([{accountId: 'b'}, {from: null}, {to: '2026-10-30'}])('differs on %o', change => {
      expect(OutflowUtils.sameScope(scope, {...scope, ...change})).toBe(false);
    });
  });
});
