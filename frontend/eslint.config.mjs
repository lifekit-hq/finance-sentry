import {createEslintConfig} from '@lifekit-hq/config/eslint';

import {structurePlugin} from './eslint-rules/file-per-concept.mjs';
import {keyboardsPlugin} from './eslint-rules/money-inputmode.mjs';
import {moneyPipePlugin} from './eslint-rules/money-pipe-location.mjs';
import {utilsPlugin} from './eslint-rules/utils-class.mjs';

export default createEslintConfig({
  selectorPrefix: 'fns',
  extraConfigs: [
    {
      // Routed surfaces that are not shell pages, so `cmn-page` (title, gutter, column) does not
      // describe them: the chat canvas fills the shell (routes.ts kind `null`), the accounts list
      // renders inside accounts-shell's `cmn-page`, and sign-in, access-denied, MCP consent and the
      // legal reading pages are centred cards outside the shell. Moving the signed-out screens onto
      // `cmn-page` (kind form/detail in routes.ts) is a visual change for its own PR.
      files: [
        'src/app/modules/agent/pages/ledger-chat/**',
        'src/app/modules/agent/pages/ledger-shell/**',
        'src/app/modules/auth/pages/**',
        'src/app/modules/bank-sync/pages/accounts-list/**',
        'src/app/modules/legal/pages/**',
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
      files: ['**/*.html'],
      plugins: {fns: keyboardsPlugin},
      rules: {'fns/money-inputmode': 'error'},
    },
  ],
});
