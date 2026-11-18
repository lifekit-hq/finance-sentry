// Local rule: a `*.pipe.ts` outside `src/app/shared/pipes/` must not be a money or currency pipe. Every
// displayed amount goes through the one `money` pipe there (ui-library-first rule, "Money display").
const MESSAGES = {
  localMoneyPipe:
    'Pipe `{{name}}` formats money outside `shared/pipes/`; use the shared `money` pipe (`MoneyUtils.format`) instead.',
};

const MONEY_NAME = /money|currency/i;
const SHARED_PIPES = /[\\/]src[\\/]app[\\/]shared[\\/]pipes[\\/]/;

// The class name and the `@Pipe({name})` string are both checked, case-insensitive.
function pipeNameOf(decorator) {
  const arg = decorator.expression.type === 'CallExpression' ? decorator.expression.arguments[0] : undefined;
  if (arg?.type !== 'ObjectExpression') return undefined;
  const prop = arg.properties.find(p => p.type === 'Property' && p.key.name === 'name');
  return prop?.value.type === 'Literal' ? String(prop.value.value) : undefined;
}

export const moneyPipeLocationRule = {
  meta: {type: 'problem', schema: [], messages: MESSAGES},
  create(context) {
    if (SHARED_PIPES.test(context.filename)) return {};

    return {
      ClassDeclaration(node) {
        const pipeDecorator = node.decorators?.find(
          d => d.expression.type === 'CallExpression' && d.expression.callee.name === 'Pipe'
        );
        if (!pipeDecorator) return;
        const className = node.id?.name ?? '';
        const pipeName = pipeNameOf(pipeDecorator) ?? '';
        if (MONEY_NAME.test(className) || MONEY_NAME.test(pipeName)) {
          context.report({node, messageId: 'localMoneyPipe', data: {name: className || pipeName}});
        }
      },
    };
  },
};

export const moneyPipePlugin = {rules: {'money-pipe-location': moneyPipeLocationRule}};
