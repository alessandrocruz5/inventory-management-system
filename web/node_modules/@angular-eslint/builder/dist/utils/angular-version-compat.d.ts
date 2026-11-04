/**
 * Shared helpers for reporting Angular major mismatches without using a
 * package-manager peer on `@angular/cli`.
 */
export declare const ANGULAR_PACKAGES_TO_CHECK: readonly ["@angular/core", "@angular/cli"];
export interface PackageJsonLike {
    dependencies?: Record<string, string>;
    devDependencies?: Record<string, string>;
    peerDependencies?: Record<string, string>;
    version?: string;
}
export interface AngularVersionMismatch {
    packageName: string;
    specifier: string;
    foundMajor: number;
}
/**
 * Best-effort major from a semver version or range (`^22.1.0`, `~21.2.0`,
 * `>= 22.0.0 < 23.0.0`, `22.1.0`).
 */
export declare function parseMajorVersion(specifier: string | undefined | null): number | null;
export declare function findDeclaredDependency(pkg: PackageJsonLike, name: string): string | undefined;
export declare function findAngularVersionMismatches(workspacePackageJson: PackageJsonLike, expectedMajor: number, installedMajors?: Partial<Record<string, number | null>>): AngularVersionMismatch[];
export declare function hasDetectableAngularVersion(workspacePackageJson: PackageJsonLike, installedMajors?: Partial<Record<string, number | null>>): boolean;
export declare function formatAngularVersionMismatchMessage(expectedMajor: number, mismatches: AngularVersionMismatch[]): string;
export declare function formatAngularVersionMatchMessage(expectedMajor: number): string;
//# sourceMappingURL=angular-version-compat.d.ts.map