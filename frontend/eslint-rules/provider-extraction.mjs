// Local rule: custom providers live in `core/providers/<name>.provider.ts` as
// `export function provideX(): EnvironmentProviders`, and `app.config.ts` lists `provideX()` calls only
// (frontend-structure rule, "Custom providers").
const MESSAGES = {
  inlineProvider:
    'app.config.ts `providers` lists `provide*()` calls only; extract this entry to `core/providers/<name>.provider.ts` as `export function provideX(): EnvironmentProviders`.',
  missingProvideFunction:
    'A `*.provider.ts` must export a `provide*` function returning `EnvironmentProviders`.',
  wrongReturnType: 'Exported `{{name}}` must declare the return type `EnvironmentProviders`.',
};

function isProvideCall(element) {
  return element?.type === 'CallExpression' && element.callee.type === 'Identifier' && element.callee.name.startsWith('provide');
}

function returnsEnvironmentProviders(fn) {
  const annotation = fn.returnType?.typeAnnotation;
  return annotation?.type === 'TSTypeReference' && annotation.typeName.type === 'Identifier' && annotation.typeName.name === 'EnvironmentProviders';
}

export const providerExtractionRule = {
  meta: {type: 'problem', schema: [], messages: MESSAGES},
  create(context) {
    const filename = context.filename.replaceAll('\\', '/');
    const isAppConfig = filename.endsWith('/app.config.ts');
    const isProviderFile = /\/core\/providers\/[^/]+\.provider\.ts$/.test(filename);
    const provideFunctions = [];

    const listeners = {};
    if (isAppConfig) {
      listeners['Property[key.name="providers"] > ArrayExpression'] = (node) => {
        for (const element of node.elements) {
          if (!isProvideCall(element)) context.report({node: element ?? node, messageId: 'inlineProvider'});
        }
      };
    }
    if (isProviderFile) {
      listeners['ExportNamedDeclaration > FunctionDeclaration'] = (node) => {
        if (node.id?.name.startsWith('provide')) provideFunctions.push(node);
      };
      listeners['Program:exit'] = (program) => {
        if (provideFunctions.length === 0) {
          context.report({node: program, loc: {line: 1, column: 0}, messageId: 'missingProvideFunction'});
        }
        for (const fn of provideFunctions) {
          if (!returnsEnvironmentProviders(fn)) context.report({node: fn, messageId: 'wrongReturnType', data: {name: fn.id.name}});
        }
      };
    }
    return listeners;
  },
};

export const providerPlugin = {rules: {'provider-extraction': providerExtractionRule}};
