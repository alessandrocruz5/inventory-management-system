import type { Rule } from '@angular-devkit/schematics';
import { type TseslintPreset } from '../utils';
interface Schema {
    project?: string;
    setParserOptionsProject?: boolean;
    tseslintPreset?: TseslintPreset;
}
export default function addESLintToProject(schema: Schema): Rule;
export {};
//# sourceMappingURL=index.d.ts.map