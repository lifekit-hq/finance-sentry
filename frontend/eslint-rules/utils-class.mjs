// Local rule: a `*.utils.ts` file holds pure helpers as a class with `public static` methods, and ships a
// sibling `*.utils.spec.ts` (frontend-structure rule, "Utility helpers").
import {existsSync} from 'node:fs';

const MESSAGES = {
  bareFunction: 'Exported function `{{name}}` must be a `public static` method of a utils class.',
  arrowConstant: 'Exported arrow-function constant `{{name}}` must be a `public static` method of a utils class.',
  instanceMember: 'Utils class member `{{name}}` must be `static`; a utils class holds no instance state.',
  constructor: 'A utils class has no constructor; helpers are `public static` methods.',
  missingSpec: 'Utils file has no sibling `{{spec}}`.',
};

const FUNCTION_TYPES = new Set(['ArrowFunctionExpression', 'FunctionExpression']);

function keyName(node, sourceCode) {
  return node.key.type === 'Identifier' ? node.key.name : sourceCode.getText(node.key);
}

export const utilsClassRule = {
  meta: {type: 'problem', schema: [], messages: MESSAGES},
  create(context) {
    const filename = context.filename;
    const sourceCode = context.sourceCode;

    function checkClass(node) {
      for (const member of node.body.body) {
        if (member.type === 'StaticBlock') continue;
        if (member.type === 'MethodDefinition' && member.kind === 'constructor') {
          context.report({node: member, messageId: 'constructor'});
        } else if (!member.static) {
          context.report({node: member, messageId: 'instanceMember', data: {name: keyName(member, sourceCode)}});
        }
      }
    }

    return {
      Program(node) {
        const spec = filename.replace(/\.utils\.ts$/, '.utils.spec.ts');
        if (spec !== filename && !existsSync(spec)) {
          context.report({node, loc: {line: 1, column: 0}, messageId: 'missingSpec', data: {spec: spec.split('/').pop()}});
        }
      },
      'ExportNamedDeclaration > FunctionDeclaration, ExportDefaultDeclaration > FunctionDeclaration'(node) {
        context.report({node, messageId: 'bareFunction', data: {name: node.id?.name ?? 'default'}});
      },
      'ExportNamedDeclaration > VariableDeclaration'(node) {
        for (const declaration of node.declarations) {
          if (declaration.init && FUNCTION_TYPES.has(declaration.init.type)) {
            context.report({node: declaration, messageId: 'arrowConstant', data: {name: sourceCode.getText(declaration.id)}});
          }
        }
      },
      'ExportNamedDeclaration > ClassDeclaration, ExportDefaultDeclaration > ClassDeclaration': checkClass,
      'Program > ClassDeclaration': checkClass,
    };
  },
};

export const utilsPlugin = {rules: {'utils-class': utilsClassRule}};
