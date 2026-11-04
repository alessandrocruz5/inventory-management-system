import { TSESLint } from '@typescript-eslint/utils';
type OrderConfig = {
    readonly Component?: string[];
    readonly Directive?: string[];
    readonly Injectable?: string[];
    readonly NgModule?: string[];
    readonly Pipe?: string[];
};
export type Options = [
    OrderConfig & {
        readonly allowUnconfiguredProperties?: boolean;
    }
];
export type MessageIds = 'incorrectOrder' | 'unconfiguredProperty';
export declare const RULE_NAME = "sort-keys-in-type-decorator";
declare const _default: TSESLint.RuleModule<MessageIds, Options, import("../utils/create-eslint-rule").RuleDocs, TSESLint.RuleListener> & {
    name: string;
};
export default _default;
export declare const RULE_DOCS_EXTENSION: {
    rationale: string;
};
//# sourceMappingURL=sort-keys-in-type-decorator.d.ts.map