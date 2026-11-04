import type { Rule } from '@angular-devkit/schematics';
/**
 * We are able to use the full, unaltered Schema directly from @schematics/angular
 * The applicable json file is copied from node_modules as a prebuiid step to ensure
 * they stay in sync.
 */
import type { Schema as AngularSchema } from '@schematics/angular/library/schema';
import { type TseslintPreset } from '../utils';
interface Schema extends AngularSchema {
    setParserOptionsProject?: boolean;
    tseslintPreset?: TseslintPreset;
}
export default function (options: Schema): Rule;
export {};
//# sourceMappingURL=index.d.ts.map