import {createEslintConfig} from '@lifekit-hq/config/eslint';

import {keyboardsPlugin} from './eslint-rules/money-inputmode.mjs';

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
      files: ['**/*.html'],
      plugins: {fns: keyboardsPlugin},
      rules: {'fns/money-inputmode': 'error'},
    },
  ],
});
