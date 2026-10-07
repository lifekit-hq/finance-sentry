// Template rule: a numeric input must declare `inputmode`, otherwise phones show the wrong keyboard
// (money fields need `decimal`, whole counts `numeric`).
const INPUT_ELEMENTS = new Set(['input', 'cmn-input']);

export const moneyInputmodeRule = {
  meta: {
    type: 'problem',
    schema: [],
    messages: {
      missingInputmode: '<{{name}} type="number"> needs an inputmode attribute (decimal for money, numeric for whole numbers).',
    },
  },
  create(context) {
    return {
      Element(node) {
        if (!INPUT_ELEMENTS.has(node.name)) {
          return;
        }
        const isNumber = node.attributes.some((a) => a.name === 'type' && a.value === 'number');
        const hasInputmode =
          node.attributes.some((a) => a.name === 'inputmode') || node.inputs.some((i) => i.name === 'inputmode');
        if (isNumber && !hasInputmode) {
          context.report({node, messageId: 'missingInputmode', data: {name: node.name}});
        }
      },
    };
  },
};

export const keyboardsPlugin = {rules: {'money-inputmode': moneyInputmodeRule}};
