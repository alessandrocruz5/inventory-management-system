import { ESLintUtils } from '@typescript-eslint/utils';
export type Options = [
    {
        readonly checkResources?: boolean;
    }
];
export type MessageIds = 'mustReadSignal';
export declare const RULE_NAME = "reactive-context-must-read-signal";
declare const _default: ESLintUtils.RuleModule<"mustReadSignal", Options, import("../utils/create-eslint-rule").RuleDocs, ESLintUtils.RuleListener> & {
    name: string;
};
export default _default;
export declare const RULE_DOCS_EXTENSION: {
    rationale: string;
};
//# sourceMappingURL=reactive-context-must-read-signal.d.ts.map