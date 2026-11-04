import { ESLintUtils } from '@typescript-eslint/utils';
type Options = [
    {
        useTypeChecking: boolean;
    }
];
export type MessageIds = 'preferSignalModel';
export declare const RULE_NAME = "prefer-signal-model";
declare const _default: ESLintUtils.RuleModule<"preferSignalModel", Options, import("../utils/create-eslint-rule").RuleDocs, ESLintUtils.RuleListener> & {
    name: string;
};
export default _default;
export declare const RULE_DOCS_EXTENSION: {
    rationale: string;
};
//# sourceMappingURL=prefer-signal-model.d.ts.map