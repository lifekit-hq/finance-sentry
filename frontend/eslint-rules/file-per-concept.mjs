// Local rule: a component or service class file defines no interface, enum or exported constant of its
// own; those live in `models/`, `constants/` or a `<page>.constants.ts` file (frontend-structure rule).
const MESSAGES = {
  inlineInterface: 'Interface `{{name}}` belongs in its own model file, not in a component or service file.',
  inlineEnum: 'Enum `{{name}}` belongs in its own model or constants file, not in a component or service file.',
  inlineConstant: 'Exported constant `{{name}}` belongs in a constants file, not in a component or service file.',
};

export const filePerConceptRule = {
  meta: {type: 'problem', schema: [], messages: MESSAGES},
  create(context) {
    return {
      TSInterfaceDeclaration(node) {
        context.report({node: node.id, messageId: 'inlineInterface', data: {name: node.id.name}});
      },
      TSEnumDeclaration(node) {
        context.report({node: node.id, messageId: 'inlineEnum', data: {name: node.id.name}});
      },
      'ExportNamedDeclaration > VariableDeclaration[kind="const"]'(node) {
        for (const declaration of node.declarations) {
          const name = declaration.id.type === 'Identifier' ? declaration.id.name : context.sourceCode.getText(declaration.id);
          context.report({node: declaration, messageId: 'inlineConstant', data: {name}});
        }
      },
    };
  },
};

export const structurePlugin = {rules: {'file-per-concept': filePerConceptRule}};
