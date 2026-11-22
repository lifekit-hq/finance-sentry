import {RuleTester} from 'eslint';
import tseslint from 'typescript-eslint';

import {sharedBoundaryRule} from './shared-boundary.mjs';

// RuleTester wires describe/it to node:test, so `node --test eslint-rules/*.test.mjs` runs these.
const ruleTester = new RuleTester({languageOptions: {parser: tseslint.parser}});

const file = '/repo/frontend/src/app/modules/budgets/pages/budgets/budgets.component.ts';

ruleTester.run('shared-boundary', sharedBoundaryRule, {
  valid: [
    {
      // Allowed: the same type imported from `shared/`.
      filename: file,
      code: `import {type Money} from '@shared/models/money/money.model';`,
    },
    {
      // Allowed: a relative path into `shared/`.
      filename: file,
      code: `import {type Money} from '../../../../shared/models/money/money.model';`,
    },
    {
      // Allowed: an import inside the same module, by alias or relative path.
      filename: file,
      code: `import {type Budget} from '@modules/budgets/models/budget/budget.model';
             import {BudgetUtils} from '../../utils/budget.utils';`,
    },
    {
      // Allowed: a service from another module is not a type, utility or enum.
      filename: file,
      code: `import {AuthStore} from '@modules/auth/store/auth.store';
             import {ScorecardService} from '../../../decisions/services/scorecard.service';`,
    },
    {
      // Allowed: `core/` and packages are not modules.
      filename: file,
      code: `import {AuthService} from '@core/auth/auth.service';
             import {signal} from '@angular/core';`,
    },
    {
      // Allowed: the rule only looks at files under `modules/`.
      filename: '/repo/frontend/src/app/shared/utils/x.utils.ts',
      code: `import {type Budget} from '@modules/budgets/models/budget/budget.model';`,
    },
  ],
  invalid: [
    {
      // Blocked: a module imports a type from another module by alias.
      filename: file,
      code: `import {type Holding} from '@modules/holdings/models/holding/holding.model';`,
      errors: [{messageId: 'crossModuleImport'}],
    },
    {
      // Blocked: the same through a relative path.
      filename: file,
      code: `import {type Holding} from '../../../holdings/models/holding/holding.model';`,
      errors: [{messageId: 'crossModuleImport'}],
    },
    {
      // Blocked: a utility and an enum from another module.
      filename: file,
      code: `import {HoldingUtils} from '@modules/holdings/utils/holding.utils';
             import {HoldingKind} from '../../../holdings/enums/holding-kind.enum';`,
      errors: [{messageId: 'crossModuleImport'}, {messageId: 'crossModuleImport'}],
    },
    {
      // Blocked: a re-export from another module.
      filename: file,
      code: `export {type Holding} from '@modules/holdings/models/holding/holding.model';`,
      errors: [{messageId: 'crossModuleImport'}],
    },
  ],
});
