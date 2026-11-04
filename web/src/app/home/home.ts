import { Component, inject } from '@angular/core';
import { AuthService } from '../core/auth/auth.service';

@Component({
  selector: 'app-home',
  template: `<p class="home">Signed in to {{ auth.user()?.tenantName }}</p>`,
  styles: `
    .home {
      padding: 16px;
    }
  `,
})
export class Home {
  protected readonly auth = inject(AuthService);
}
