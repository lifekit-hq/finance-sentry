import {RuleTester} from 'eslint';
import tseslint from 'typescript-eslint';

import {
  noComponentStateRule,
  noErrorCodeLadderRule,
  noSetIntervalRule,
} from './composition-discipline.mjs';

// RuleTester wires describe/it to node:test, so `node --test eslint-rules/*.test.mjs` runs these.
const ruleTester = new RuleTester({languageOptions: {parser: tseslint.parser}});

ruleTester.run('no-component-state', noComponentStateRule, {
  valid: [
    {
      // Allowed: bindings to store signals, inputs and derivations are not held state.
      code: `class C {
        readonly isLoading = this.store.isLoading;
        readonly loading = input(false);
        readonly errorMessage = computed(() => this.store.error());
      }`,
    },
    {
      // Allowed: fields that are not feature state.
      code: `class C { readonly form = new FormGroup({}); readonly open = signal(false); }`,
    },
  ],
  invalid: [
    {
      // Blocked: a loading flag held in the component.
      code: `class C { readonly isLoading = signal(false); }`,
      errors: [{messageId: 'componentState', data: {name: 'isLoading'}}],
    },
    {
      // Blocked: a plain mutable error field and fetched data.
      code: `class C { errorMessage = ''; items = signal<Row[]>([]); }`,
      errors: [{messageId: 'componentState'}, {messageId: 'componentState'}],
    },
  ],
});

ruleTester.run('no-set-interval', noSetIntervalRule, {
  valid: [
    {
      // Allowed: `timer(...)` polling, and a store method that happens to be named setInterval.
      code: `timer(0, 5000).subscribe(); store.setInterval('1D');`,
    },
  ],
  invalid: [
    {
      // Blocked: the global timer.
      code: `setInterval(() => this.refresh(), 5000);`,
      errors: [{messageId: 'setInterval'}],
    },
    {
      // Blocked: through the global object.
      code: `window.setInterval(tick, 1000);`,
      errors: [{messageId: 'setInterval'}],
    },
  ],
});

ruleTester.run('no-error-code-ladder', noErrorCodeLadderRule, {
  valid: [
    {
      // Allowed: a lookup in a constant set.
      code: `const dup = DUPLICATE_CODES.has(code);`,
    },
    {
      // Allowed: one comparison, and comparisons that are not on a code operand.
      code: `const a = code === 'POLLING'; const b = status === 'a' || status === 'b';`,
    },
    {
      // Allowed: a lone `if` on a code.
      code: `if (code === 'A') { run(); } else { stop(); }`,
    },
  ],
  invalid: [
    {
      // Blocked: an `||` ladder over one code, reported once.
      code: `const dup = code === 'ALREADY_CONNECTED' || code === 'BINANCE_DUPLICATE' || code === 'X';`,
      errors: [{messageId: 'errorLadder', data: {operand: 'code'}}],
    },
    {
      // Blocked: an if / else-if ladder on a member code.
      code: `if (err.code === 'A') { a(); } else if (err.code === 'B') { b(); }`,
      errors: [{messageId: 'errorLadder', data: {operand: 'code'}}],
    },
    {
      // Blocked: a switch with several string cases on a code.
      code: `switch (errorCode) { case 'A': return 1; case 'B': return 2; }`,
      errors: [{messageId: 'errorLadder', data: {operand: 'errorCode'}}],
    },
  ],
});
