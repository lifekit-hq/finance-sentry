import {RuleTester} from 'eslint';
import tseslint from 'typescript-eslint';

import {moneyPipeLocationRule} from './money-pipe-location.mjs';

// RuleTester wires describe/it to node:test, so `node --test eslint-rules/*.test.mjs` runs these.
const ruleTester = new RuleTester({languageOptions: {parser: tseslint.parser}});

const SHARED = '/repo/frontend/src/app/shared/pipes/money.pipe.ts';
const LOCAL = '/repo/frontend/src/app/modules/bank-sync/pipes/balance-money.pipe.ts';

ruleTester.run('money-pipe-location', moneyPipeLocationRule, {
  valid: [
    {
      // Allowed: the shared money pipe lives in shared/pipes/.
      filename: SHARED,
      code: `@Pipe({name: 'money'}) export class MoneyPipe { transform(v: number) { return v; } }`,
    },
    {
      // Allowed: a non-money pipe in a module.
      filename: '/repo/frontend/src/app/modules/bank-sync/pipes/position-quantity.pipe.ts',
      code: `@Pipe({name: 'positionQuantity'}) export class PositionQuantityPipe { transform(v: number) { return v; } }`,
    },
  ],
  invalid: [
    {
      // Blocked: a per-module money pipe, matched on the class name.
      filename: LOCAL,
      code: `@Pipe({name: 'balance'}) export class BalanceMoneyPipe { transform(v: number) { return v; } }`,
      errors: [{messageId: 'localMoneyPipe', data: {name: 'BalanceMoneyPipe'}}],
    },
    {
      // Blocked: matched on the pipe name alone, case-insensitive.
      filename: LOCAL,
      code: `@Pipe({name: 'fxCurrency'}) export class FxPipe { transform(v: number) { return v; } }`,
      errors: [{messageId: 'localMoneyPipe', data: {name: 'FxPipe'}}],
    },
  ],
});
