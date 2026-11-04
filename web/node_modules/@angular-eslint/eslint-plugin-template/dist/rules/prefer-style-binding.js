"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.RULE_DOCS_EXTENSION = exports.RULE_NAME = void 0;
const utils_1 = require("@angular-eslint/utils");
const create_eslint_rule_1 = require("../utils/create-eslint-rule");
exports.RULE_NAME = 'prefer-style-binding';
exports.default = (0, create_eslint_rule_1.createESLintRule)({
    name: exports.RULE_NAME,
    meta: {
        type: 'suggestion',
        docs: {
            description: 'Suggests using [style] bindings over ngStyle where applicable',
        },
        schema: [],
        messages: {
            preferStyleBinding: 'Consider using [style] or [style.property] bindings instead of [ngStyle] where applicable.',
        },
        defaultOptions: [],
    },
    create(context) {
        const parserServices = (0, utils_1.getTemplateParserServices)(context);
        return {
            'BoundAttribute[name="ngStyle"]'(node) {
                const loc = parserServices.convertNodeSourceSpanToLoc(node.sourceSpan);
                context.report({
                    messageId: 'preferStyleBinding',
                    loc,
                });
            },
        };
    },
});
exports.RULE_DOCS_EXTENSION = {
    rationale: 'For simple cases, [style] and [style.property] bindings offer a more straightforward syntax with better performance than ngStyle, and the Angular style guide recommends them over the NgStyle directive. However, ngStyle should still be used when you need mutations on objects, as style bindings compare the bound object by reference and only apply updates when a new object instance is provided. Note that ngStyle also accepts unit suffixes on object keys (for example `{ "max-width.px": width }`), which [style] object bindings do not support; express these as [style.max-width.px] bindings instead. See https://angular.dev/style-guide#prefer-class-and-style-over-ngclass-and-ngstyle and https://angular.dev/guide/templates/binding#css-class-and-style-property-bindings for more information. This rule helps identify potential simplification opportunities but should be applied judiciously based on your specific needs.',
};
