import {RuleTester} from 'eslint';
import tseslint from 'typescript-eslint';

import {providerExtractionRule} from './provider-extraction.mjs';

// RuleTester wires describe/it to node:test, so `node --test eslint-rules/*.test.mjs` runs these.
const ruleTester = new RuleTester({languageOptions: {parser: tseslint.parser}});

const appConfig = '/repo/frontend/src/app/app.config.ts';
const providerFile = '/repo/frontend/src/app/core/providers/locale.provider.ts';

ruleTester.run('provider-extraction', providerExtractionRule, {
  valid: [
    {
      // Allowed: app.config.ts lists `provide*()` calls only.
      filename: appConfig,
      code: `export const appConfig = {providers: [provideRouter(ROUTES), provideAppLocale()]};`,
    },
    {
      // Allowed: a provider file exporting `provide*` returning EnvironmentProviders (other exports are fine).
      filename: providerFile,
      code: `export const KEY = 'k';
             export function provideAppLocale(): EnvironmentProviders { return makeEnvironmentProviders([{provide: LOCALE_ID, useValue: 'en'}]); }`,
    },
    {
      // Allowed: the rule only looks at app.config.ts and core/providers/*.provider.ts.
      filename: '/repo/frontend/src/app/modules/x/x.component.ts',
      code: `export const c = {providers: [{provide: A, useValue: 1}]};`,
    },
  ],
  invalid: [
    {
      // Blocked: an inline `{provide: ...}` entry in app.config.ts.
      filename: appConfig,
      code: `export const appConfig = {providers: [provideAppLocale(), {provide: LOCALE_ID, useValue: 'en'}]};`,
      errors: [{messageId: 'inlineProvider'}],
    },
    {
      // Blocked: a bare class or non-`provide` call.
      filename: appConfig,
      code: `export const appConfig = {providers: [MyService, makeEnvironmentProviders([])]};`,
      errors: [{messageId: 'inlineProvider'}, {messageId: 'inlineProvider'}],
    },
    {
      // Blocked: a provider file with no `provide*` function.
      filename: providerFile,
      code: `export const KEY = 'k';`,
      errors: [{messageId: 'missingProvideFunction'}],
    },
    {
      // Blocked: `provide*` function without the EnvironmentProviders return type.
      filename: providerFile,
      code: `export function provideAppLocale() { return makeEnvironmentProviders([]); }`,
      errors: [{messageId: 'wrongReturnType', data: {name: 'provideAppLocale'}}],
    },
  ],
});
