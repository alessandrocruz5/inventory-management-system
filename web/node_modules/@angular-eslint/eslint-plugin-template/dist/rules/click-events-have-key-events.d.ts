export type Options = [
    {
        readonly ignoreWithDirectives?: string[];
        readonly requireKeyCode?: boolean;
        readonly allowedKeyCodes?: string[];
    }
];
export type MessageIds = 'clickEventsHaveKeyEvents' | 'clickEventsHaveKeyCode' | 'clickEventsHaveAllowedKeyCode';
export declare const RULE_NAME = "click-events-have-key-events";
declare const _default: import("@typescript-eslint/utils/ts-eslint").RuleModule<MessageIds, Options, import("../utils/create-eslint-rule").RuleDocs, import("@typescript-eslint/utils/ts-eslint").RuleListener> & {
    name: string;
};
export default _default;
export declare const RULE_DOCS_EXTENSION: {
    rationale: string;
};
//# sourceMappingURL=click-events-have-key-events.d.ts.map