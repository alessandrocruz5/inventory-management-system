"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.RULE_DOCS_EXTENSION = exports.RULE_NAME = void 0;
const utils_1 = require("@angular-eslint/utils");
const create_eslint_rule_1 = require("../utils/create-eslint-rule");
const utils_2 = require("@typescript-eslint/utils");
exports.RULE_NAME = 'no-duplicates-in-metadata-arrays';
exports.default = (0, create_eslint_rule_1.createESLintRule)({
    name: exports.RULE_NAME,
    meta: {
        type: 'suggestion',
        docs: {
            description: 'Ensures that metadata arrays do not contain duplicate entries.',
        },
        schema: [],
        messages: {
            noDuplicatesInMetadataArrays: 'Entry is duplicated in metadata array',
        },
        defaultOptions: [],
    },
    create(context) {
        const selectors = [
            // https://angular.dev/api/core/NgModule
            `${utils_1.Selectors.MODULE_CLASS_DECORATOR} Property[key.name=${(0, utils_1.toPattern)([
                'providers',
                'declarations',
                'imports',
                'exports',
            ])}] > ArrayExpression`,
            // https://angular.dev/api/core/Component
            `${utils_1.Selectors.COMPONENT_CLASS_DECORATOR} Property[key.name=${(0, utils_1.toPattern)([
                'imports',
                'providers',
                'viewProviders',
                'styleUrls',
            ])}] > ArrayExpression`,
            // https://angular.dev/api/core/Directive
            `${utils_1.Selectors.DIRECTIVE_CLASS_DECORATOR} Property[key.name=${(0, utils_1.toPattern)([
                'providers',
            ])}] > ArrayExpression`,
        ].join(',');
        return {
            [selectors]({ elements }) {
                getDuplicateItems(elements).forEach((duplicateImport) => {
                    context.report({
                        node: duplicateImport,
                        messageId: 'noDuplicatesInMetadataArrays',
                    });
                });
            },
        };
    },
});
function getDuplicateItems(elements) {
    const items = elements.filter((element) => utils_2.ASTUtils.isIdentifier(element) ||
        (element?.type === 'Literal' && typeof element.value === 'string'));
    const uniqueItemKeys = new Set();
    const duplicateItems = new Array();
    items.forEach((item) => {
        const key = item.type === 'Identifier' ? item.name : item.value;
        if (uniqueItemKeys.has(key)) {
            duplicateItems.push(item);
        }
        else {
            uniqueItemKeys.add(key);
        }
    });
    return duplicateItems;
}
exports.RULE_DOCS_EXTENSION = {
    rationale: 'Duplicate entries in Angular metadata arrays like providers, imports, declarations, or exports serve no purpose and indicate a mistake. When the same provider is listed twice in a providers array, Angular will create it twice, potentially leading to unexpected behavior with singleton services. Duplicate imports or declarations simply waste bundle size and make the metadata harder to read. These duplicates typically occur from copy-paste errors, merge conflicts, or refactoring mistakes. Catching these duplicates early prevents subtle bugs and keeps module metadata clean and understandable.',
};
