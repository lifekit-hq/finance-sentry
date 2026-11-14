import {createEslintConfig} from '@lifekit-hq/config/eslint';

import {keyboardsPlugin} from './eslint-rules/money-inputmode.mjs';

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
      files: ['**/*.html'],
      plugins: {fns: keyboardsPlugin},
      rules: {'fns/money-inputmode': 'error'},
    },
  ],
});
