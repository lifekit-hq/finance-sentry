// Local rule: a type, utility or enum used across feature modules lives in `shared/`, so a file under
// `modules/<a>/` never imports a model, type, enum or utility file from `modules/<b>/`
// (constitution CC8, Principle VI.5). Services, stores and components are not covered: they are
// not types, utilities or enums.
import path from 'node:path';

const MESSAGES = {
  crossModuleImport:
    '`{{source}}` imports from module `{{target}}` into module `{{own}}`; a type, utility or enum used across modules lives in `shared/`. Move it there and import it from `@shared/*`.',
};

// Where a module keeps its types, enums and utilities (constitution VI.5: `models/`, `*.utils.ts`).
const SHARED_KIND = /\/(?:models|enums|utils)\/|\.(?:model|types|enum|utils)$/;

const MODULES_SEGMENT = '/src/app/modules/';

function moduleOf(file) {
  const index = file.indexOf(MODULES_SEGMENT);
  if (index === -1) return null;
  return file.slice(index + MODULES_SEGMENT.length).split('/')[0] || null;
}

// The module an import specifier lands in: `@modules/<name>/...`, or a relative path resolved
// against the importing file. Anything else (`@shared/*`, `@core/*`, packages) is not a module.
function targetOf(specifier, filename) {
  if (specifier.startsWith('@modules/')) {
    return {
      module: specifier.split('/')[1] || null,
      path: `/${specifier.slice('@modules/'.length)}`,
    };
  }
  if (specifier.startsWith('.')) {
    const resolved = path.posix.resolve(path.posix.dirname(filename), specifier);
    return {module: moduleOf(resolved), path: resolved};
  }
  return null;
}

export const sharedBoundaryRule = {
  meta: {type: 'problem', schema: [], messages: MESSAGES},
  create(context) {
    const filename = context.filename.replaceAll('\\', '/');
    const own = moduleOf(filename);
    if (!own) return {};

    const check = node => {
      const source = node.source?.value;
      if (typeof source !== 'string') return;
      const target = targetOf(source, filename);
      if (target?.module && target.module !== own && SHARED_KIND.test(target.path)) {
        context.report({
          node: node.source,
          messageId: 'crossModuleImport',
          data: {source, target: target.module, own},
        });
      }
    };
    return {
      ImportDeclaration: check,
      ExportAllDeclaration: check,
      ExportNamedDeclaration: check,
    };
  },
};

export const sharedBoundaryPlugin = {rules: {'shared-boundary': sharedBoundaryRule}};
