/**
 * - `false`: never allowed.
 * - `true`: always allowed.
 * - `'dynamic'`: allowed only when the bound value is not a constant
 *   (a literal, or an expression built exclusively from literals). Constant
 *   values could live in a stylesheet, so they are still reported.
 */
export type AllowOption = boolean | 'dynamic';
export type Options = [
    {
        readonly allowNgStyle?: AllowOption;
        readonly allowBindToStyle?: AllowOption;
    }
];
export type MessageIds = 'noInlineStyles';
export declare const RULE_NAME = "no-inline-styles";
declare const _default: import("@typescript-eslint/utils/ts-eslint").RuleModule<"noInlineStyles", Options, import("../utils/create-eslint-rule").RuleDocs, import("@typescript-eslint/utils/ts-eslint").RuleListener> & {
    name: string;
};
export default _default;
export declare const RULE_DOCS_EXTENSION: {
    rationale: string;
};
//# sourceMappingURL=no-inline-styles.d.ts.map