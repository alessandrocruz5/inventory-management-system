"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.createESLintRule = void 0;
const utils_1 = require("@typescript-eslint/utils");
exports.createESLintRule = utils_1.ESLintUtils.RuleCreator((ruleName) => `https://github.com/angular-eslint/angular-eslint/blob/main/packages/eslint-plugin-template/docs/rules/${ruleName}.md`);
