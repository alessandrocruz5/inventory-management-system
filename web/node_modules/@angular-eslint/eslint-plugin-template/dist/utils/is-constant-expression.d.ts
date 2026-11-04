import { type AST } from '@angular-eslint/bundled-angular-compiler';
/**
 * Determines whether a template expression is a constant, i.e. its value is
 * fully determined by the template source alone and can never change at
 * runtime.
 *
 * An expression is constant when it is built exclusively from literals
 * (strings, numbers, booleans, `null`, `undefined`, literal maps, literal
 * arrays and template literals) combined with pure operators (arithmetic,
 * logical, comparison, conditional, `typeof`, `void`, `!`, `!!`, parentheses).
 *
 * Anything that reads component state (property reads, keyed reads), performs
 * a call, or applies a pipe is dynamic. Unknown node types are also treated as
 * dynamic, so that this helper fails open rather than reporting false
 * positives.
 */
export declare function isConstantExpression(node: AST): boolean;
//# sourceMappingURL=is-constant-expression.d.ts.map