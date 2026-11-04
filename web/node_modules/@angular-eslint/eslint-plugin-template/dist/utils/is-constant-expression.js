"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.isConstantExpression = isConstantExpression;
const bundled_angular_compiler_1 = require("@angular-eslint/bundled-angular-compiler");
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
function isConstantExpression(node) {
    if (node instanceof bundled_angular_compiler_1.ASTWithSource) {
        return isConstantExpression(node.ast);
    }
    if (node instanceof bundled_angular_compiler_1.LiteralPrimitive || node instanceof bundled_angular_compiler_1.EmptyExpr) {
        return true;
    }
    if (node instanceof bundled_angular_compiler_1.LiteralMap) {
        return node.values.every(isConstantExpression);
    }
    if (node instanceof bundled_angular_compiler_1.LiteralArray) {
        return node.expressions.every(isConstantExpression);
    }
    if (node instanceof bundled_angular_compiler_1.Interpolation || node instanceof bundled_angular_compiler_1.TemplateLiteral) {
        return node.expressions.every(isConstantExpression);
    }
    if (node instanceof bundled_angular_compiler_1.ParenthesizedExpression) {
        return isConstantExpression(node.expression);
    }
    // `Unary` extends `Binary`, so it must be checked first.
    if (node instanceof bundled_angular_compiler_1.Unary) {
        return isConstantExpression(node.expr);
    }
    if (node instanceof bundled_angular_compiler_1.Binary) {
        return isConstantExpression(node.left) && isConstantExpression(node.right);
    }
    if (node instanceof bundled_angular_compiler_1.Conditional) {
        return (isConstantExpression(node.condition) &&
            isConstantExpression(node.trueExp) &&
            isConstantExpression(node.falseExp));
    }
    if (node instanceof bundled_angular_compiler_1.PrefixNot ||
        node instanceof bundled_angular_compiler_1.TypeofExpression ||
        node instanceof bundled_angular_compiler_1.VoidExpression ||
        node instanceof bundled_angular_compiler_1.NonNullAssert) {
        return isConstantExpression(node.expression);
    }
    return false;
}
