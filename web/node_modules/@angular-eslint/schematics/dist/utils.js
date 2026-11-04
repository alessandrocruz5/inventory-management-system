"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.DEFAULT_TS_ESLINT_PRESET = exports.TS_ESLINT_PRESETS = exports.defaultFlatConfigNames = void 0;
exports.readJsonInTree = readJsonInTree;
exports.updateJsonInTree = updateJsonInTree;
exports.getTargetsConfigFromProject = getTargetsConfigFromProject;
exports.addESLintTargetToProject = addESLintTargetToProject;
exports.visitNotIgnoredFiles = visitNotIgnoredFiles;
exports.isTypeCheckedTseslintPreset = isTypeCheckedTseslintPreset;
exports.resolveTseslintPreset = resolveTseslintPreset;
exports.shouldEnableProjectService = shouldEnableProjectService;
exports.warnIfTypeCheckedPreset = warnIfTypeCheckedPreset;
exports.createStringifiedRootESLintConfig = createStringifiedRootESLintConfig;
exports.createESLintConfigForProject = createESLintConfigForProject;
exports.sortObjectByKeys = sortObjectByKeys;
exports.determineTargetProjectName = determineTargetProjectName;
exports.updateSchematicCollections = updateSchematicCollections;
exports.updateSchematicDefaults = updateSchematicDefaults;
exports.resolveRootESLintConfigPath = resolveRootESLintConfigPath;
exports.determineNewProjectESLintConfigContentAndExtension = determineNewProjectESLintConfigContentAndExtension;
const core_1 = require("@angular-devkit/core");
const schematics_1 = require("@angular-devkit/schematics");
const ignore_1 = __importDefault(require("ignore"));
const strip_json_comments_1 = __importDefault(require("strip-json-comments"));
const DEFAULT_PREFIX = 'app';
/**
 * The conventional flat config file names that ESLint resolves automatically
 * and that these schematics scaffold by default.
 *
 * NOTE: this is NOT a restriction on what an ESLint flat config file may be
 * named - a flat config can have any name. We only use these conventional names
 * when detecting an existing root config or resolving its path during code
 * generation.
 */
exports.defaultFlatConfigNames = [
    'eslint.config.js',
    'eslint.config.mjs',
    'eslint.config.cjs',
    'eslint.config.ts',
    'eslint.config.mts',
    'eslint.config.cts',
];
/**
 * This method is specifically for reading JSON files in a Tree
 * @param host The host tree
 * @param path The path to the JSON file
 * @returns The JSON data in the file.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function readJsonInTree(host, path) {
    if (!host.exists(path)) {
        throw new Error(`Cannot find ${path}`);
    }
    const contents = (0, strip_json_comments_1.default)(host.read(path).toString('utf-8'));
    try {
        return JSON.parse(contents);
    }
    catch (e) {
        throw new Error(`Cannot parse ${path}: ${e instanceof Error ? e.message : ''}`, { cause: e });
    }
}
/**
 * This method is specifically for updating JSON in a Tree
 * @param path Path of JSON file in the Tree
 * @param callback Manipulation of the JSON data
 * @returns A rule which updates a JSON file in a Tree
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function updateJsonInTree(path, callback) {
    return (host, context) => {
        if (!host.exists(path)) {
            host.create(path, serializeJson(callback({}, context)));
            return host;
        }
        host.overwrite(path, serializeJson(callback(readJsonInTree(host, path), context)));
        return host;
    };
}
function getTargetsConfigFromProject(projectConfig) {
    if (!projectConfig) {
        return null;
    }
    if (projectConfig.architect) {
        return projectConfig.architect;
    }
    // "targets" is an undocumented but supported alias of "architect"
    if (projectConfig.targets) {
        return projectConfig.targets;
    }
    return null;
}
function offsetFromRoot(fullPathToSourceDir) {
    const parts = (0, core_1.normalize)(fullPathToSourceDir).split('/');
    let offset = '';
    for (let i = 0; i < parts.length; ++i) {
        offset += '../';
    }
    return offset;
}
function serializeJson(json) {
    return `${JSON.stringify(json, null, 2)}\n`;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function updateWorkspaceInTree(callback) {
    return (host, context) => {
        const path = 'angular.json';
        host.overwrite(path, serializeJson(callback(readJsonInTree(host, path), context, host)));
        return host;
    };
}
function addESLintTargetToProject(projectName, targetName) {
    return updateWorkspaceInTree((workspaceJson, _, tree) => {
        const existingProjectConfig = workspaceJson.projects[projectName];
        let lintFilePatternsRoot = '';
        // Default Angular CLI project at the root of the workspace
        if (existingProjectConfig.root === '') {
            lintFilePatternsRoot = existingProjectConfig.sourceRoot || 'src';
        }
        else {
            lintFilePatternsRoot = existingProjectConfig.root;
        }
        const eslintTargetConfig = {
            builder: '@angular-eslint/builder:lint',
            options: {
                lintFilePatterns: [
                    `${lintFilePatternsRoot}/**/*.ts`,
                    `${lintFilePatternsRoot}/**/*.html`,
                ],
            },
        };
        let eslintConfig;
        if (existingProjectConfig.root !== '') {
            const rootConfigPath = resolveRootESLintConfigPath(tree);
            if (!rootConfigPath ||
                (!rootConfigPath.endsWith('js') && !rootConfigPath.endsWith('ts'))) {
                throw new Error('Root ESLint config must be a JavaScript/TypeScript file (.js,.mjs,.cjs,.ts,.mts,.cts)');
            }
            const { ext } = determineNewProjectESLintConfigContentAndExtension(tree, rootConfigPath, existingProjectConfig.root);
            const flatConfigPath = (0, core_1.join)(existingProjectConfig.root, `eslint.config.${ext}`);
            if (tree.exists(flatConfigPath)) {
                eslintConfig = flatConfigPath;
            }
        }
        eslintTargetConfig.options.eslintConfig = eslintConfig;
        existingProjectConfig.architect = existingProjectConfig.architect || {};
        existingProjectConfig.architect[targetName] = eslintTargetConfig;
        return workspaceJson;
    });
}
/**
 * Utility to act on all files in a tree that are not ignored by git.
 */
function visitNotIgnoredFiles(visitor, dir = (0, core_1.normalize)('')) {
    return (host, context) => {
        let ig;
        if (host.exists('.gitignore')) {
            ig = (0, ignore_1.default)();
            ig.add(host.read('.gitignore').toString());
        }
        function visit(_dir) {
            if (_dir && ig?.ignores(_dir)) {
                return;
            }
            const dirEntry = host.getDir(_dir);
            dirEntry.subfiles.forEach((file) => {
                if (ig?.ignores((0, core_1.join)(_dir, file))) {
                    return;
                }
                const maybeRule = visitor((0, core_1.join)(_dir, file), host, context);
                if (maybeRule) {
                    (0, schematics_1.callRule)(maybeRule, host, context).subscribe();
                }
            });
            dirEntry.subdirs.forEach((subdir) => {
                visit((0, core_1.join)(_dir, subdir));
            });
        }
        visit(dir);
    };
}
/**
 * typescript-eslint shared config presets that the schematics can scaffold.
 *
 * Type-checked variants also enable `parserOptions.projectService` in the
 * generated root config, which is required for those rules to run.
 */
exports.TS_ESLINT_PRESETS = [
    'recommended',
    'strict',
    'recommendedTypeChecked',
    'strictTypeChecked',
];
exports.DEFAULT_TS_ESLINT_PRESET = 'recommended';
function isTypeCheckedTseslintPreset(preset) {
    return preset === 'recommendedTypeChecked' || preset === 'strictTypeChecked';
}
function resolveTseslintPreset(value) {
    if (value && exports.TS_ESLINT_PRESETS.includes(value)) {
        return value;
    }
    return exports.DEFAULT_TS_ESLINT_PRESET;
}
function shouldEnableProjectService(setParserOptionsProject, tseslintPreset = exports.DEFAULT_TS_ESLINT_PRESET) {
    return setParserOptionsProject || isTypeCheckedTseslintPreset(tseslintPreset);
}
function warnIfTypeCheckedPreset(context, tseslintPreset) {
    if (!isTypeCheckedTseslintPreset(tseslintPreset)) {
        return;
    }
    context.logger.warn(`Typed linting is enabled because tseslintPreset="${tseslintPreset}". This makes ESLint more powerful but slower than the default recommended preset. See https://github.com/angular-eslint/angular-eslint/blob/main/docs/RULES_REQUIRING_TYPE_INFORMATION.md`);
}
function getTsEslintExtendsSnippet(preset) {
    switch (preset) {
        case 'strict':
            return `tseslint.configs.strict,
      tseslint.configs.stylistic,`;
        case 'recommendedTypeChecked':
            return `tseslint.configs.recommendedTypeChecked,
      tseslint.configs.stylisticTypeChecked,`;
        case 'strictTypeChecked':
            return `tseslint.configs.strictTypeChecked,
      tseslint.configs.stylisticTypeChecked,`;
        case 'recommended':
        default:
            return `tseslint.configs.recommended,
      tseslint.configs.stylistic,`;
    }
}
function getProjectServiceBlock(enabled, includeTypeCheckedComment) {
    if (!enabled) {
        return '';
    }
    const comment = includeTypeCheckedComment
        ? `
    // Enable typed linting via the typescript-eslint Project Service.
    // This is slower than untyped linting; see https://typescript-eslint.io/getting-started/typed-linting`
        : '';
    return `${comment}
    languageOptions: {
      parserOptions: {
        projectService: true,
      },
    },`;
}
function createStringifiedRootESLintConfig(prefix, isESM, options = {}) {
    const tseslintPreset = resolveTseslintPreset(options.tseslintPreset);
    const includeProjectService = shouldEnableProjectService(options.setParserOptionsProject ?? false, tseslintPreset);
    return `// @ts-check
${isESM ? 'import eslint from "@eslint/js";' : 'const eslint = require("@eslint/js");'}
${isESM ? 'import { defineConfig } from "eslint/config";' : 'const { defineConfig } = require("eslint/config");'}
${isESM ? 'import tseslint from "typescript-eslint";' : 'const tseslint = require("typescript-eslint");'}
${isESM ? 'import angular from "angular-eslint";' : 'const angular = require("angular-eslint");'}

${isESM ? 'export default' : 'module.exports ='} defineConfig([
  {
    files: ["**/*.ts"],${getProjectServiceBlock(includeProjectService, isTypeCheckedTseslintPreset(tseslintPreset))}
    extends: [
      eslint.configs.recommended,
      ${getTsEslintExtendsSnippet(tseslintPreset)}
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: ${prefix
        ? `{
      "@angular-eslint/directive-selector": [
        "error",
        {
          type: "attribute",
          prefix: "${prefix}",
          style: "camelCase",
        },
      ],
      "@angular-eslint/component-selector": [
        "error",
        {
          type: "element",
          prefix: "${prefix}",
          style: "kebab-case",
        },
      ],
    }`
        : '{}'},
  },
  {
    files: ["**/*.html"],
    extends: [
      angular.configs.templateRecommended,
      angular.configs.templateAccessibility,
    ],
    rules: {},
  }
]);
`;
}
function createStringifiedProjectESLintConfig(projectRoot, projectType, prefix, setParserOptionsProject, isESM, rootConfigPath) {
    const relativeRootConfigPath = offsetFromRoot(projectRoot) + rootConfigPath;
    return `// @ts-check
${isESM ? 'import { defineConfig } from "eslint/config";' : 'const { defineConfig } = require("eslint/config");'}
${isESM ? `import rootConfig from "${relativeRootConfigPath}";` : `const rootConfig = require("${relativeRootConfigPath}");`}

${isESM ? 'export default' : 'module.exports ='} defineConfig([
  ...rootConfig,
  {
    files: ["**/*.ts"],${setParserOptionsProject
        ? `
    languageOptions: {
      parserOptions: {
        projectService: true,
      },
    },`
        : ''}
    rules: {
      "@angular-eslint/directive-selector": [
        "error",
        {
          type: "attribute",
          prefix: "${prefix}",
          style: "camelCase",
        },
      ],
      "@angular-eslint/component-selector": [
        "error",
        {
          type: "element",
          prefix: "${prefix}",
          style: "kebab-case",
        },
      ],
    },
  },
  {
    files: ["**/*.html"],
    rules: {},
  }
]);
`;
}
function createESLintConfigForProject(projectName, setParserOptionsProject, tseslintPreset = exports.DEFAULT_TS_ESLINT_PRESET) {
    return (tree, context) => {
        const resolvedPreset = resolveTseslintPreset(tseslintPreset);
        const enableProjectService = shouldEnableProjectService(setParserOptionsProject, resolvedPreset);
        warnIfTypeCheckedPreset(context, resolvedPreset);
        const angularJSON = readJsonInTree(tree, 'angular.json');
        const { root: projectRoot, projectType, prefix, } = angularJSON.projects[projectName];
        const alreadyHasRootFlatConfig = exports.defaultFlatConfigNames.some((name) => tree.exists(name));
        const rootConfigOptions = {
            tseslintPreset: resolvedPreset,
            setParserOptionsProject: enableProjectService,
        };
        /**
         * If the root is an empty string it must be the initial project created at the
         * root by the Angular CLI's workspace schematic
         */
        if (projectRoot === '') {
            return createRootESLintConfigFile(prefix || DEFAULT_PREFIX, rootConfigOptions);
        }
        const rules = [];
        // If, for whatever reason, the root eslint.config.* doesn't exist yet, create it
        if (!alreadyHasRootFlatConfig) {
            rules.push(createRootESLintConfigFile(prefix || DEFAULT_PREFIX, rootConfigOptions));
        }
        const rootConfigPath = resolveRootESLintConfigPath(tree) ?? 'eslint.config.js';
        rules.push((host) => {
            const { isESM, ext } = determineNewProjectESLintConfigContentAndExtension(host, rootConfigPath, projectRoot);
            return host.create((0, core_1.join)((0, core_1.normalize)(projectRoot), `eslint.config.${ext}`), createStringifiedProjectESLintConfig(projectRoot, projectType || 'library', prefix || DEFAULT_PREFIX, enableProjectService, isESM, rootConfigPath));
        });
        return (0, schematics_1.chain)(rules);
    };
}
function createRootESLintConfigFile(prefix, options = {}) {
    return (tree) => {
        // If the root package.json uses type: module, generate ESM content
        const packageJson = readJsonInTree(tree, 'package.json');
        const isESM = packageJson.type === 'module';
        return tree.create('eslint.config.js', createStringifiedRootESLintConfig(prefix, isESM, options));
    };
}
function sortObjectByKeys(obj) {
    return Object.keys(obj)
        .sort()
        .reduce((result, key) => {
        return {
            ...result,
            [key]: obj[key],
        };
    }, {});
}
/**
 * To make certain schematic usage conversion more ergonomic, if the user does not specify a project
 * and only has a single project in their angular.json we will just go ahead and use that one.
 */
function determineTargetProjectName(tree, maybeProject) {
    if (maybeProject) {
        return maybeProject;
    }
    const workspaceJson = readJsonInTree(tree, 'angular.json');
    const projects = Object.keys(workspaceJson.projects);
    if (projects.length === 1) {
        return projects[0];
    }
    return null;
}
/**
 * See `schematicCollections` docs here:
 * https://github.com/angular/angular-cli/blob/8431b3f0769b5f95b9e13807a09293d820c4b017/docs/specifications/schematic-collections-config.md
 */
function updateSchematicCollections(angularJson, collectionName) {
    angularJson.cli = angularJson.cli || {};
    angularJson.cli.schematicCollections =
        angularJson.cli.schematicCollections || [];
    // The first matching schematic will be used, so we unshift rather than push
    angularJson.cli.schematicCollections.unshift(collectionName);
    // Delete old defaultCollection property if applicable
    delete angularJson.cli.defaultCollection;
    return angularJson;
}
function updateSchematicDefaults(angularJson, schematicFullName, defaultValues) {
    angularJson.schematics = angularJson.schematics || {};
    angularJson.schematics[schematicFullName] =
        angularJson.schematics[schematicFullName] || {};
    angularJson.schematics[schematicFullName] = {
        ...angularJson.schematics[schematicFullName],
        ...defaultValues,
    };
    return angularJson;
}
function resolveRootESLintConfigPath(tree) {
    for (const name of exports.defaultFlatConfigNames) {
        if (tree.exists(name)) {
            return name;
        }
    }
    return null;
}
function determineNewProjectESLintConfigContentAndExtension(tree, rootConfigPath, projectRoot) {
    try {
        /**
         * The decision on exactly what config format an extension to use is based on the format
         * used in the root config, and the project's local package.json type (if any).
         */
        const isRootESM = rootConfigPath.endsWith('.cjs')
            ? false
            : rootConfigPath.endsWith('.mjs') ||
                readJsonInTree(tree, 'package.json').type === 'module';
        const projectPackageJsonPath = (0, core_1.join)((0, core_1.normalize)(projectRoot), 'package.json');
        let isESM = isRootESM;
        let ext = 'js';
        if (tree.exists(projectPackageJsonPath)) {
            const projectPackageJson = readJsonInTree(tree, projectPackageJsonPath);
            const isProjectESM = projectPackageJson.type === 'module';
            if (isRootESM && !isProjectESM) {
                ext = 'mjs';
                isESM = true;
            }
            else if (!isRootESM && isProjectESM) {
                ext = 'cjs';
            }
        }
        return {
            isESM,
            ext,
        };
    }
    catch {
        return {
            isESM: false,
            ext: 'js',
        };
    }
}
