"use strict";
/**
 * Shared helpers for reporting Angular major mismatches without using a
 * package-manager peer on `@angular/cli`.
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.ANGULAR_PACKAGES_TO_CHECK = void 0;
exports.parseMajorVersion = parseMajorVersion;
exports.findDeclaredDependency = findDeclaredDependency;
exports.findAngularVersionMismatches = findAngularVersionMismatches;
exports.hasDetectableAngularVersion = hasDetectableAngularVersion;
exports.formatAngularVersionMismatchMessage = formatAngularVersionMismatchMessage;
exports.formatAngularVersionMatchMessage = formatAngularVersionMatchMessage;
exports.ANGULAR_PACKAGES_TO_CHECK = [
    '@angular/core',
    '@angular/cli',
];
/**
 * Best-effort major from a semver version or range (`^22.1.0`, `~21.2.0`,
 * `>= 22.0.0 < 23.0.0`, `22.1.0`).
 */
function parseMajorVersion(specifier) {
    if (!specifier) {
        return null;
    }
    const trimmed = specifier.trim();
    if (!trimmed ||
        trimmed === '*' ||
        trimmed === 'latest' ||
        /^(workspace|catalog|file|link|portal|git|http|https):/i.test(trimmed)) {
        return null;
    }
    const match = trimmed.match(/(\d+)/);
    if (!match) {
        return null;
    }
    const major = Number(match[1]);
    // 0.x (including the e2e publish version `0.0.0-e2e`) is not an
    // Angular-aligned major; treat it as undetectable rather than v0.
    if (major === 0) {
        return null;
    }
    return major;
}
function findDeclaredDependency(pkg, name) {
    return (pkg.dependencies?.[name] ??
        pkg.devDependencies?.[name] ??
        pkg.peerDependencies?.[name]);
}
function findAngularVersionMismatches(workspacePackageJson, expectedMajor, installedMajors) {
    const mismatches = [];
    for (const name of exports.ANGULAR_PACKAGES_TO_CHECK) {
        const declared = findDeclaredDependency(workspacePackageJson, name);
        const installedMajor = installedMajors?.[name];
        const major = installedMajor ?? parseMajorVersion(declared);
        if (major == null || major === expectedMajor) {
            continue;
        }
        mismatches.push({
            packageName: name,
            specifier: declared ?? `v${major}`,
            foundMajor: major,
        });
    }
    return mismatches;
}
function hasDetectableAngularVersion(workspacePackageJson, installedMajors) {
    return exports.ANGULAR_PACKAGES_TO_CHECK.some((name) => {
        const installed = installedMajors?.[name];
        if (installed != null) {
            return true;
        }
        return (parseMajorVersion(findDeclaredDependency(workspacePackageJson, name)) !==
            null);
    });
}
function formatAngularVersionMismatchMessage(expectedMajor, mismatches) {
    const details = mismatches
        .map((mismatch) => `  - ${mismatch.packageName}@${mismatch.specifier} (v${mismatch.foundMajor})`)
        .join('\n');
    return `
angular-eslint v${expectedMajor} is intended for Angular v${expectedMajor}.
This workspace is using a different Angular major:
${details}

See https://github.com/angular-eslint/angular-eslint/blob/main/docs/ANGULAR_VERSION_SUPPORT.md
`.trim();
}
function formatAngularVersionMatchMessage(expectedMajor) {
    return `angular-eslint v${expectedMajor} matches this workspace's Angular v${expectedMajor}.`;
}
