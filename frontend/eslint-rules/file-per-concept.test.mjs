import {RuleTester} from 'eslint';
import tseslint from 'typescript-eslint';

import {filePerConceptRule} from './file-per-concept.mjs';

// RuleTester wires describe/it to node:test, so `node --test eslint-rules/` runs these.
const ruleTester = new RuleTester({languageOptions: {parser: tseslint.parser}});

ruleTester.run('file-per-concept', filePerConceptRule, {
  valid: [
    // Allowed: the class file imports its model and constant, and keeps only private helpers local.
    `import {type Row} from './row.model';
     import {ROW_LIMIT} from './row.constants';
     const TILES: readonly Row[] = [];
     export class RowService { limit = ROW_LIMIT; }`,
    // Allowed: exported functions and types are not interfaces, enums or constants.
    `export type RowId = string; export function rowId(): RowId { return ''; }`,
  ],
  invalid: [
    {
      // Blocked: an inline interface in a component or service file.
      code: `interface ReasonRow { code: string; }
             export class RowComponent {}`,
      errors: [{messageId: 'inlineInterface', data: {name: 'ReasonRow'}}],
    },
    {
      code: `export interface HoldingsResult { total: number; }`,
      errors: [{messageId: 'inlineInterface', data: {name: 'HoldingsResult'}}],
    },
    {
      code: `export enum Mode { A, B }`,
      errors: [{messageId: 'inlineEnum', data: {name: 'Mode'}}],
    },
    {
      code: `export const PAGE_SIZE = 20;`,
      errors: [{messageId: 'inlineConstant', data: {name: 'PAGE_SIZE'}}],
    },
  ],
});
