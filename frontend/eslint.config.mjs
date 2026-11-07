import {createEslintConfig} from '@lifekit-hq/config/eslint';

import {keyboardsPlugin} from './eslint-rules/money-inputmode.mjs';

export default createEslintConfig({
  selectorPrefix: 'fns',
  extraConfigs: [
    {
      // lifekit-common 4.4 warns on every routed page not rooted on `cmn-page` (24 today), and lint
      // runs with --max-warnings 0. Pages adopt the template area by area (the route list in
      // e2e/screens/routes.ts names each page's kind), so the rule stays off until the last
      // area has adopted it; then delete this entry and the rule is the library's warning again.
      rules: {'lifekit/page-root': 'off'},
    },
    {
      files: ['**/*.html'],
      plugins: {fns: keyboardsPlugin},
      rules: {'fns/money-inputmode': 'error'},
    },
  ],
});
