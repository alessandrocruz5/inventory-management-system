"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.reportAngularVersionCompatibility = reportAngularVersionCompatibility;
const node_fs_1 = require("node:fs");
const node_path_1 = require("node:path");
const angular_version_compat_1 = require("./angular-version-compat");
function readWorkspacePackageJson(workspaceRoot) {
    try {
        return JSON.parse((0, node_fs_1.readFileSync)((0, node_path_1.join)(workspaceRoot, 'package.json'), 'utf8'));
    }
    catch {
        return {};
    }
}
/**
 * Read an installed package version from this workspace only (no walk-up
 * into parent node_modules, which would lie in monorepo tests and nested
 * workspaces).
 */
function readInstalledMajor(workspaceRoot, packageName) {
    try {
        const pkgJsonPath = (0, node_path_1.join)(workspaceRoot, 'node_modules', ...packageName.split('/'), 'package.json');
        const pkg = JSON.parse((0, node_fs_1.readFileSync)(pkgJsonPath, 'utf8'));
        return (0, angular_version_compat_1.parseMajorVersion)(pkg.version);
    }
    catch {
        return null;
    }
}
/**
 * Warn when the workspace Angular major does not match this builder's major.
 * Does not fail the lint; package managers should not be the compatibility UI.
 */
function reportAngularVersionCompatibility(workspaceRoot, expectedMajor, warn) {
    const workspacePackageJson = readWorkspacePackageJson(workspaceRoot);
    const installedMajors = {};
    for (const name of angular_version_compat_1.ANGULAR_PACKAGES_TO_CHECK) {
        installedMajors[name] = readInstalledMajor(workspaceRoot, name);
    }
    const mismatches = (0, angular_version_compat_1.findAngularVersionMismatches)(workspacePackageJson, expectedMajor, installedMajors);
    if (mismatches.length === 0) {
        return;
    }
    warn((0, angular_version_compat_1.formatAngularVersionMismatchMessage)(expectedMajor, mismatches));
}
