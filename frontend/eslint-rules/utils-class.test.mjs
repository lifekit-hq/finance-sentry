import {mkdtempSync, writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';

import {RuleTester} from 'eslint';
import tseslint from 'typescript-eslint';

import {utilsClassRule} from './utils-class.mjs';

// RuleTester wires describe/it to node:test, so `node --test eslint-rules/*.test.mjs` runs these.
const ruleTester = new RuleTester({languageOptions: {parser: tseslint.parser}});

// A utils file with a sibling spec on disk, and one without.
const dir = mkdtempSync(join(tmpdir(), 'utils-class-'));
const withSpec = join(dir, 'money.utils.ts');
writeFileSync(join(dir, 'money.utils.spec.ts'), '');
const withoutSpec = join(dir, 'orphan.utils.ts');

ruleTester.run('utils-class', utilsClassRule, {
  valid: [
    {
      // Allowed: a class of `public static` methods with a sibling spec.
      filename: withSpec,
      code: `export class MoneyUtils {
               public static round(value: number): number { return Math.round(value); }
               private static pad(s: string): string { return s; }
             }`,
    },
    {
      // Allowed: private module-level helpers and exported types are not exported functions.
      filename: withSpec,
      code: `export type Cents = number;
             function pad(s: string): string { return s; }
             export class MoneyUtils { public static fmt(s: string): string { return pad(s); } }`,
    },
  ],
  invalid: [
    {
      // Blocked: a bare exported function.
      filename: withSpec,
      code: `export function round(value: number): number { return Math.round(value); }`,
      errors: [{messageId: 'bareFunction', data: {name: 'round'}}],
    },
    {
      filename: withSpec,
      code: `export const round = (value: number): number => Math.round(value);`,
      errors: [{messageId: 'arrowConstant', data: {name: 'round'}}],
    },
    {
      filename: withSpec,
      code: `export class MoneyUtils { public round(value: number): number { return value; } }`,
      errors: [{messageId: 'instanceMember', data: {name: 'round'}}],
    },
    {
      filename: withSpec,
      code: `export class MoneyUtils { constructor() {} public static round(v: number): number { return v; } }`,
      errors: [{messageId: 'constructor'}],
    },
    {
      // Blocked: no sibling `*.utils.spec.ts`.
      filename: withoutSpec,
      code: `export class OrphanUtils { public static id(v: number): number { return v; } }`,
      errors: [{messageId: 'missingSpec', data: {spec: 'orphan.utils.spec.ts'}}],
    },
  ],
});
