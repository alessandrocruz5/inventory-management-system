import type { TSESLint } from '@typescript-eslint/utils';
export type Options = [{
    readonly allowExplicitOnPush: boolean;
}];
export type MessageIds = 'preferOnPushComponentChangeDetection' | 'suggestRemoveChangeDetection' | 'redundantOnPushComponentChangeDetection';
export declare const RULE_NAME = "prefer-on-push-component-change-detection";
declare const _default: TSESLint.RuleModule<MessageIds, Options, import("../utils/create-eslint-rule").RuleDocs, TSESLint.RuleListener> & {
    name: string;
};
export default _default;
export declare const RULE_DOCS_EXTENSION: {
    rationale: string;
};
//# sourceMappingURL=prefer-on-push-component-change-detection.d.ts.map