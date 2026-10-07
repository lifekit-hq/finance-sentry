import {createEslintConfig} from '@lifekit-hq/config/eslint';

import {keyboardsPlugin} from './eslint-rules/money-inputmode.mjs';

export default createEslintConfig({
  selectorPrefix: 'fns',
  extraConfigs: [
    {
      files: ['**/*.html'],
      plugins: {fns: keyboardsPlugin},
      rules: {'fns/money-inputmode': 'error'},
    },
  ],
});
