"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.RULE_DOCS_EXTENSION = exports.RULE_NAME = void 0;
const utils_1 = require("@angular-eslint/utils");
const create_eslint_rule_1 = require("../utils/create-eslint-rule");
const has_ignored_directive_1 = require("../utils/has-ignored-directive");
const get_dom_elements_1 = require("../utils/get-dom-elements");
const is_hidden_from_screen_reader_1 = require("../utils/is-hidden-from-screen-reader");
const is_interactive_element_1 = require("../utils/is-interactive-element");
const is_presentation_role_1 = require("../utils/is-presentation-role");
const get_attribute_value_1 = require("../utils/get-attribute-value");
exports.RULE_NAME = 'click-events-have-key-events';
const DEFAULT_OPTIONS = {
    ignoreWithDirectives: [],
    requireKeyCode: false,
    allowedKeyCodes: [],
};
const KEY_EVENTS = ['keyup', 'keydown', 'keypress'];
exports.default = (0, create_eslint_rule_1.createESLintRule)({
    name: exports.RULE_NAME,
    meta: {
        type: 'suggestion',
        docs: {
            description: '[Accessibility] Ensures that the click event is accompanied with at least one key event keyup, keydown or keypress.',
        },
        schema: [
            {
                type: 'object',
                properties: {
                    ignoreWithDirectives: {
                        type: 'array',
                        items: { type: 'string' },
                        uniqueItems: true,
                        description: 'Directive names that, when present on the element, cause it to be ignored. Entries wrapped in slashes, e.g. `/^tui/`, are treated as regular expressions.',
                        default: DEFAULT_OPTIONS.ignoreWithDirectives,
                    },
                    requireKeyCode: {
                        type: 'boolean',
                        description: 'Require key events to specify a key via a pseudo-event, e.g. `(keydown.enter)`. A bare `(keydown)` will not satisfy the rule.',
                        default: DEFAULT_OPTIONS.requireKeyCode,
                    },
                    allowedKeyCodes: {
                        type: 'array',
                        items: { type: 'string' },
                        uniqueItems: true,
                        description: 'Only key events whose key (the last segment of the pseudo-event, e.g. `enter` in `(keydown.shift.enter)`) is in this list satisfy the rule. Implies `requireKeyCode`.',
                        default: DEFAULT_OPTIONS.allowedKeyCodes,
                    },
                },
                additionalProperties: false,
            },
        ],
        messages: {
            clickEventsHaveKeyEvents: 'click must be accompanied by either keyup, keydown or keypress event for accessibility.',
            clickEventsHaveKeyCode: 'click must be accompanied by a keyup, keydown or keypress event that specifies a key (e.g. `(keydown.enter)`) for accessibility.',
            clickEventsHaveAllowedKeyCode: 'click must be accompanied by a keyup, keydown or keypress event for one of the allowed keys ({{allowedKeyCodes}}) for accessibility.',
        },
        defaultOptions: [DEFAULT_OPTIONS],
    },
    create(context, [{ ignoreWithDirectives, requireKeyCode, allowedKeyCodes }]) {
        const normalizedAllowedKeyCodes = (allowedKeyCodes ?? []).map((keyCode) => keyCode.toLowerCase());
        const hasIgnoredDirective = (0, has_ignored_directive_1.createIgnoredDirectiveMatcher)(ignoreWithDirectives);
        return {
            Element(node) {
                if (!(0, get_dom_elements_1.getDomElements)().has(node.name.toLowerCase())) {
                    return;
                }
                if (hasIgnoredDirective(node) ||
                    (0, is_presentation_role_1.isPresentationRole)(node) ||
                    (0, is_hidden_from_screen_reader_1.isHiddenFromScreenReader)(node) ||
                    (0, is_interactive_element_1.isInherentlyInteractiveElement)(node)) {
                    return;
                }
                // The final case that should be ignored is element which is not inherently interactive, but which has an interactive role.
                // TODO: extend utils with this check (and make it include all interactive roles)
                const role = (0, get_attribute_value_1.getAttributeValue)(node, 'role');
                if (role === 'button') {
                    return;
                }
                let hasClick = false, hasKeyEvent = false, hasSatisfyingKeyEvent = false;
                for (const output of node.outputs) {
                    hasClick = hasClick || output.name === 'click';
                    if (!isKeyEvent(output)) {
                        continue;
                    }
                    hasKeyEvent = true;
                    const keyCode = getKeyCode(output);
                    if (normalizedAllowedKeyCodes.length > 0) {
                        hasSatisfyingKeyEvent =
                            hasSatisfyingKeyEvent ||
                                (keyCode !== undefined &&
                                    normalizedAllowedKeyCodes.includes(keyCode));
                    }
                    else if (requireKeyCode) {
                        hasSatisfyingKeyEvent =
                            hasSatisfyingKeyEvent || keyCode !== undefined;
                    }
                    else {
                        hasSatisfyingKeyEvent = true;
                    }
                }
                if (!hasClick || hasSatisfyingKeyEvent) {
                    return;
                }
                const parserServices = (0, utils_1.getTemplateParserServices)(context);
                const loc = parserServices.convertNodeSourceSpanToLoc(node.sourceSpan);
                if (!hasKeyEvent) {
                    context.report({
                        loc,
                        messageId: 'clickEventsHaveKeyEvents',
                    });
                }
                else if (normalizedAllowedKeyCodes.length > 0) {
                    context.report({
                        loc,
                        messageId: 'clickEventsHaveAllowedKeyCode',
                        data: { allowedKeyCodes: normalizedAllowedKeyCodes.join(', ') },
                    });
                }
                else {
                    context.report({
                        loc,
                        messageId: 'clickEventsHaveKeyCode',
                    });
                }
            },
        };
    },
});
function isKeyEvent({ name }) {
    return KEY_EVENTS.some((keyEvent) => name === keyEvent || name.startsWith(`${keyEvent}.`));
}
/**
 * Returns the key targeted by a key pseudo-event, e.g. `enter` for
 * `(keydown.shift.enter)`, or `undefined` for a bare `(keydown)`.
 */
function getKeyCode({ name }) {
    const segments = name.split('.');
    if (segments.length < 2) {
        return undefined;
    }
    const keyCode = segments[segments.length - 1].toLowerCase();
    return keyCode.length > 0 ? keyCode : undefined;
}
exports.RULE_DOCS_EXTENSION = {
    rationale: 'Elements with click handlers must also be operable via keyboard for users who cannot use a mouse. This includes users with motor disabilities, users of assistive technologies, and keyboard-only users. While native interactive elements like buttons and links are keyboard-accessible by default (activated by Enter or Space keys), non-interactive elements with click handlers (like divs or spans) are not. For such elements, you must add keyboard event handlers (keyup, keydown, or keypress) that perform the same action as the click handler. Alternatively, use a semantic button element which handles this automatically. This is a WCAG Level A requirement for keyboard accessibility.',
};
