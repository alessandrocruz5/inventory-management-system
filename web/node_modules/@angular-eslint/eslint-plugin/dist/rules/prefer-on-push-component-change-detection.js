"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.RULE_DOCS_EXTENSION = exports.RULE_NAME = void 0;
const utils_1 = require("@angular-eslint/utils");
const create_eslint_rule_1 = require("../utils/create-eslint-rule");
exports.RULE_NAME = 'prefer-on-push-component-change-detection';
const METADATA_PROPERTY_NAME = 'changeDetection';
const STRATEGY_ON_PUSH = 'ChangeDetectionStrategy.OnPush';
const DEFAULT_OPTIONS = { allowExplicitOnPush: true };
exports.default = (0, create_eslint_rule_1.createESLintRule)({
    name: exports.RULE_NAME,
    meta: {
        type: 'suggestion',
        docs: {
            description: `Ensures components do not opt out of the default \`${STRATEGY_ON_PUSH}\` change detection strategy`,
            recommended: 'recommended',
        },
        fixable: 'code',
        hasSuggestions: true,
        schema: [
            {
                type: 'object',
                properties: {
                    allowExplicitOnPush: {
                        type: 'boolean',
                        default: DEFAULT_OPTIONS.allowExplicitOnPush,
                        description: `Whether to allow a component to set \`${METADATA_PROPERTY_NAME}: ${STRATEGY_ON_PUSH}\` explicitly even though it is now the default.`,
                    },
                },
                additionalProperties: false,
            },
        ],
        messages: {
            preferOnPushComponentChangeDetection: `Components should not opt out of the default \`${STRATEGY_ON_PUSH}\` change detection strategy`,
            suggestRemoveChangeDetection: `Remove \`${METADATA_PROPERTY_NAME}\` to use the default (\`${STRATEGY_ON_PUSH}\`)`,
            redundantOnPushComponentChangeDetection: `\`${METADATA_PROPERTY_NAME}: ${STRATEGY_ON_PUSH}\` is redundant because \`${STRATEGY_ON_PUSH}\` is the default change detection strategy`,
        },
        defaultOptions: [DEFAULT_OPTIONS],
    },
    create(context, [{ allowExplicitOnPush }]) {
        const sourceCode = context.sourceCode;
        const changeDetectionMetadataProperty = utils_1.Selectors.metadataProperty(METADATA_PROPERTY_NAME);
        const changeDetectionStrategyProperty = `${utils_1.Selectors.COMPONENT_CLASS_DECORATOR} > CallExpression > ObjectExpression > ${changeDetectionMetadataProperty}[value.object.name='ChangeDetectionStrategy']`;
        const onPushOptOutProperty = `${changeDetectionStrategyProperty}[value.property.name!='OnPush']`;
        const redundantOnPushProperty = `${changeDetectionStrategyProperty}[value.property.name='OnPush']`;
        function removeChangeDetectionProperty(node, fixer) {
            const importDeclarations = utils_1.ASTUtils.getImportDeclarations(node, '@angular/core') ?? [];
            return [
                utils_1.RuleFixes.getNodeToCommaRemoveFix(sourceCode, node, fixer),
                utils_1.RuleFixes.getImportRemoveFix(sourceCode, importDeclarations, 'ChangeDetectionStrategy', fixer),
            ].filter(utils_1.isNotNullOrUndefined);
        }
        return {
            [onPushOptOutProperty](node) {
                const { value } = node;
                // The selector guarantees a `ChangeDetectionStrategy.<member>` value;
                // this narrows the type for the fixer.
                if (!utils_1.ASTUtils.isMemberExpression(value)) {
                    return;
                }
                context.report({
                    node: value.property,
                    messageId: 'preferOnPushComponentChangeDetection',
                    suggest: [
                        {
                            messageId: 'suggestRemoveChangeDetection',
                            fix: (fixer) => removeChangeDetectionProperty(node, fixer),
                        },
                    ],
                });
            },
            [redundantOnPushProperty](node) {
                if (allowExplicitOnPush) {
                    return;
                }
                const { value } = node;
                if (!utils_1.ASTUtils.isMemberExpression(value)) {
                    return;
                }
                context.report({
                    node: value.property,
                    messageId: 'redundantOnPushComponentChangeDetection',
                    fix: (fixer) => removeChangeDetectionProperty(node, fixer),
                });
            },
        };
    },
});
exports.RULE_DOCS_EXTENSION = {
    rationale: `As of Angular v22, \`${STRATEGY_ON_PUSH}\` is the default change detection strategy: a component that does not specify \`${METADATA_PROPERTY_NAME}\` is checked using OnPush. \
This brings new code in line with zoneless being the default and with Angular's goal of performance by default, and means it is no longer necessary to set \`${STRATEGY_ON_PUSH}\` explicitly. \
The previous default, \`ChangeDetectionStrategy.Default\`, has been renamed to \`ChangeDetectionStrategy.Eager\`. \
When you run \`ng update\`, the v22 migration adds an explicit \`ChangeDetectionStrategy.Eager\` to existing components that relied on the old implicit default, so that they keep behaving as before.\n\n\
By default the rule reports components that explicitly opt out of OnPush by setting \`${METADATA_PROPERTY_NAME}\` to \`ChangeDetectionStrategy.Eager\` (or the deprecated \`ChangeDetectionStrategy.Default\`) — including the components the migration marked as \`Eager\` — so you can review them and adopt OnPush where it is safe to do so. \
Because omitting \`${METADATA_PROPERTY_NAME}\` already gives you OnPush, declaring \`${STRATEGY_ON_PUSH}\` explicitly is redundant; set \`allowExplicitOnPush\` to \`false\` to have the rule flag and autofix that too. \
Note that switching a component from eager checking to OnPush can change its runtime behaviour, so apply the suggestion deliberately and make sure the component uses immutable data patterns (creating new object references when data changes).`,
};
