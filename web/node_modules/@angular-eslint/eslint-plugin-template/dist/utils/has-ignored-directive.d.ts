import type { TmplAstElement } from '@angular-eslint/bundled-angular-compiler';
/**
 * Compiles an `ignoreWithDirectives` option into a predicate that reports
 * whether an element carries one of the ignored directives.
 *
 * Entries wrapped in slashes, e.g. `/^tui/`, are treated as regular
 * expressions (an optional flags suffix is supported, e.g. `/^tui/i`).
 * All other entries are matched exactly against the directive name.
 */
export declare function createIgnoredDirectiveMatcher(ignoreWithDirectives: readonly string[] | undefined): (element: TmplAstElement) => boolean;
//# sourceMappingURL=has-ignored-directive.d.ts.map