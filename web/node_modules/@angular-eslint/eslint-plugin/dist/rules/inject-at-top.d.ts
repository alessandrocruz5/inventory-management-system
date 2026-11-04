export type Options = [
    {
        readonly additionalInjectFunctions?: readonly string[];
    }
];
export type MessageIds = 'injectAtTop';
export declare const RULE_NAME = "inject-at-top";
declare const _default: import("@typescript-eslint/utils/ts-eslint").RuleModule<"injectAtTop", Options, import("../utils/create-eslint-rule").RuleDocs, import("@typescript-eslint/utils/ts-eslint").RuleListener> & {
    name: string;
};
export default _default;
export declare const RULE_DOCS_EXTENSION: {
    rationale: string;
};
//# sourceMappingURL=inject-at-top.d.ts.map