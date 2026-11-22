import {createEslintConfig} from '@lifekit-hq/config/eslint';

import {structurePlugin} from './eslint-rules/file-per-concept.mjs';
import {keyboardsPlugin} from './eslint-rules/money-inputmode.mjs';
import {moneyPipePlugin} from './eslint-rules/money-pipe-location.mjs';
import {providerPlugin} from './eslint-rules/provider-extraction.mjs';
import {sharedBoundaryPlugin} from './eslint-rules/shared-boundary.mjs';
import {utilsPlugin} from './eslint-rules/utils-class.mjs';

export default createEslintConfig({
  selectorPrefix: 'fns',
  extraConfigs: [
    {
      // Routed surfaces that are not shell pages, so `cmn-page` (title, gutter, column) does not
      // describe them: the chat canvas fills the shell (routes.ts kind `null`), the accounts list
      // renders inside accounts-shell's `cmn-page`. The signed-out screens (sign-in, access-denied,
      // MCP consent, privacy, terms) are `cmn-page` roots and are linted like any other page.
      files: [
        'src/app/modules/agent/pages/ledger-chat/**',
        'src/app/modules/agent/pages/ledger-shell/**',
        'src/app/modules/bank-sync/pages/accounts-list/**',
      ],
      rules: {'lifekit/page-root': 'off'},
    },
    {
      // Interfaces, enums and exported constants live in their own model/constants files, so a
      // component or service class file stays about the class (replaces the frontend-structure prose).
      files: ['src/app/**/*.component.ts', 'src/app/**/*.service.ts'],
      plugins: {structure: structurePlugin},
      rules: {'structure/file-per-concept': 'error'},
    },
    {
      // Pure helpers live in a `*.utils.ts` class with `public static` methods and a sibling spec
      // (replaces the frontend-structure "Utility helpers" prose).
      files: ['src/app/**/*.utils.ts'],
      plugins: {utils: utilsPlugin},
      rules: {'utils/utils-class': 'error'},
    },
    {
      // Every displayed amount goes through `MoneyUtils.format` and the one `money` pipe in
      // `shared/pipes/` (replaces the ui-library-first "Money display" prose). The library rule was
      // `warn`; the local rule rejects a money/currency pipe anywhere else.
      files: ['src/app/**/*.ts', 'src/app/**/*.html'],
      rules: {'lifekit/no-local-currency-format': 'error'},
    },
    {
      files: ['src/app/**/*.pipe.ts'],
      plugins: {money: moneyPipePlugin},
      rules: {'money/money-pipe-location': 'error'},
    },
    {
      // Custom providers live in `core/providers/*.provider.ts` as `provideX(): EnvironmentProviders`, and
      // `app.config.ts` lists `provide*()` calls only (replaces the frontend-structure "Custom providers" prose).
      files: ['src/app/app.config.ts', 'src/app/core/providers/*.provider.ts'],
      plugins: {providers: providerPlugin},
      rules: {'providers/provider-extraction': 'error'},
    },
    {
      // A type, utility or enum used across feature modules lives in `shared/`, so a module never
      // imports from another module (constitution CC8, Principle VI.5).
      files: ['src/app/modules/**/*.ts'],
      plugins: {boundary: sharedBoundaryPlugin},
      rules: {'boundary/shared-boundary': 'error'},
    },
    {
      files: ['**/*.html'],
      plugins: {fns: keyboardsPlugin},
      rules: {'fns/money-inputmode': 'error'},
    },
  ],
});
