// Local rules for constitution CC6 (Principle VI): feature state is not held in a component, polling
// does not use `setInterval`, and error codes are not mapped in inline ladders.
const MESSAGES = {
  componentState:
    '`{{name}}` is feature state held in a component; hold it in the feature `signalStore()` under `modules/<feature>/store/` and bind the store signal in the template.',
  setInterval:
    'Polling does not use `setInterval`; use `timer(...)` inside an `rxMethod` in the feature store effects.',
  errorLadder:
    'Error codes are not mapped in an inline ladder over `{{operand}}`; look the code up in a constant map or the error-messages registry.',
};

// ---- composition/no-component-state -------------------------------------------------------------
// A component class field that is mutable feature state: a loading/saving/error flag or a fetched-data
// field initialised with `signal(...)`, `model(...)`, a literal or a subject. `input()`, `computed()`,
// `inject()` and store-signal aliases (`this.store.x`) are bindings, not held state.
const STATE_NAME =
  /^(?:is)?(?:loading|saving|submitting|busy)$|^(?:error|errorMessage|loadError)$|^(?:data|items|result|results|response)$/i;
const HELD_STATE_FACTORY = new Set(['signal', 'model', 'BehaviorSubject', 'ReplaySubject']);

function holdsState(value) {
  if (!value) return true; // `isLoading = ;` is not valid, but `isLoading: boolean;` declares a mutable field
  if (value.type === 'Literal' || value.type === 'TemplateLiteral') return true;
  if (value.type === 'ArrayExpression' || value.type === 'ObjectExpression') return true;
  if (value.type === 'NewExpression' && value.callee.type === 'Identifier')
    return HELD_STATE_FACTORY.has(value.callee.name);
  if (value.type === 'CallExpression' && value.callee.type === 'Identifier')
    return HELD_STATE_FACTORY.has(value.callee.name);
  return false;
}

export const noComponentStateRule = {
  meta: {type: 'problem', schema: [], messages: MESSAGES},
  create(context) {
    return {
      PropertyDefinition(node) {
        if (node.static || node.computed || node.key.type !== 'Identifier') return;
        if (!STATE_NAME.test(node.key.name)) return;
        if (!holdsState(node.value)) return;
        context.report({node, messageId: 'componentState', data: {name: node.key.name}});
      },
    };
  },
};

// ---- composition/no-set-interval ----------------------------------------------------------------
// The global timer only: `store.setInterval(...)` (a store method named after a price interval) is fine.
export const noSetIntervalRule = {
  meta: {type: 'problem', schema: [], messages: MESSAGES},
  create(context) {
    return {
      CallExpression(node) {
        const callee = node.callee;
        const isGlobal = callee.type === 'Identifier' && callee.name === 'setInterval';
        const isOnGlobalObject =
          callee.type === 'MemberExpression' &&
          !callee.computed &&
          callee.property.name === 'setInterval' &&
          callee.object.type === 'Identifier' &&
          (callee.object.name === 'window' || callee.object.name === 'globalThis');
        if (isGlobal || isOnGlobalObject) context.report({node, messageId: 'setInterval'});
      },
    };
  },
};

// ---- composition/no-error-code-ladder -----------------------------------------------------------
// Two or more string-literal comparisons of one `*code` operand chained with `||`, an `if / else if`
// chain, or a `switch` with two or more string cases on it.
const CODE_NAME = /code$/i;

function operandName(node) {
  if (node.type === 'Identifier') return node.name;
  if (node.type === 'MemberExpression' && !node.computed && node.property.type === 'Identifier')
    return node.property.name;
  if (node.type === 'CallExpression') return operandName(node.callee);
  return null;
}

function isStringLiteral(node) {
  return node.type === 'Literal' && typeof node.value === 'string';
}

// `code === 'X'` or `'X' === code` (also `==`): the code operand's name, else null.
function codeComparison(node) {
  if (node.type !== 'BinaryExpression' || !['===', '==', '!==', '!='].includes(node.operator))
    return null;
  const [subject, literal] = isStringLiteral(node.right)
    ? [node.left, node.right]
    : [node.right, node.left];
  if (!isStringLiteral(literal)) return null;
  const name = operandName(subject);
  return name && CODE_NAME.test(name) ? name : null;
}

function collectOr(node, out, seen) {
  if (node.type === 'LogicalExpression' && node.operator === '||') {
    seen?.add(node);
    collectOr(node.left, out, seen);
    collectOr(node.right, out, seen);
  } else {
    out.push(node);
  }
}

export const noErrorCodeLadderRule = {
  meta: {type: 'problem', schema: [], messages: MESSAGES},
  create(context) {
    const reported = new WeakSet();
    const report = (node, operand) => {
      if (reported.has(node)) return;
      reported.add(node);
      context.report({node, messageId: 'errorLadder', data: {operand}});
    };
    return {
      LogicalExpression(node) {
        if (node.operator !== '||' || reported.has(node)) return;
        const terms = [];
        collectOr(node, terms, reported);
        const counts = new Map();
        for (const term of terms) {
          const name = codeComparison(term);
          if (name) counts.set(name, (counts.get(name) ?? 0) + 1);
        }
        const ladder = [...counts].find(([, count]) => count >= 2);
        if (!ladder) return;
        // Mark nested `||` so one ladder reports once, on its outermost expression.
        reported.delete(node);
        report(node, ladder[0]);
      },
      IfStatement(node) {
        if (node.parent?.type === 'IfStatement' && node.parent.alternate === node) return;
        const name = node.test && codeComparison(node.test);
        if (!name) return;
        const next = node.alternate;
        if (next?.type === 'IfStatement' && codeComparison(next.test) === name) report(node, name);
      },
      SwitchStatement(node) {
        const name = operandName(node.discriminant);
        if (!name || !CODE_NAME.test(name)) return;
        const cases = node.cases.filter(c => c.test && isStringLiteral(c.test));
        if (cases.length >= 2) report(node, name);
      },
    };
  },
};

export const compositionPlugin = {
  rules: {
    'no-component-state': noComponentStateRule,
    'no-set-interval': noSetIntervalRule,
    'no-error-code-ladder': noErrorCodeLadderRule,
  },
};
