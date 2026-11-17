import { BreakpointObserver } from '@angular/cdk/layout';
import { inject, type Signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';

export const NARROW_QUERY = '(max-width: 759.98px)';

/**
 * True below the 760 px breakpoint (handoff README "Responsive"). Call in an injection context.
 * Under jsdom there is no `matchMedia`, so this is `false`; tests that need the mobile branch provide
 * `{ provide: BreakpointObserver, useValue: { observe: () => of({ matches: true, breakpoints: {} }), isMatched: () => true } }`.
 */
export function injectIsNarrow(): Signal<boolean> {
  const observer = inject(BreakpointObserver);
  return toSignal(observer.observe(NARROW_QUERY).pipe(map((r) => r.matches)), {
    initialValue: observer.isMatched(NARROW_QUERY),
  });
}
