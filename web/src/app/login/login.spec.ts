import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { PROBLEM_MESSAGES } from '../core/problems/problem-messages';
import { Login } from './login';

describe('Login', () => {
  const login = vi.fn();

  beforeEach(async () => {
    login.mockReset();
    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { login, user: signal(null) } },
      ],
    }).compileComponents();
  });

  function setup() {
    const fixture = TestBed.createComponent(Login);
    const el = fixture.nativeElement as HTMLElement;
    const submit = () => el.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    const fill = async (email: string, password: string) => {
      const [emailInput, passwordInput] = Array.from(el.querySelectorAll('input'));
      emailInput.value = email;
      emailInput.dispatchEvent(new Event('input'));
      passwordInput.value = password;
      passwordInput.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    return { fixture, el, submit, fill };
  }

  it('disables Sign in when the form is empty', async () => {
    const { fixture, submit } = setup();
    await fixture.whenStable();
    expect(submit().disabled).toBe(true);
  });

  it('navigates to / after a successful login', async () => {
    login.mockResolvedValue({});
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const { fixture, submit, fill } = setup();
    await fixture.whenStable();
    await fill('staff@demo.test', 'secret');
    expect(submit().disabled).toBe(false);
    submit().click();
    await fixture.whenStable();
    expect(login).toHaveBeenCalledWith('staff@demo.test', 'secret');
    expect(navigate).toHaveBeenCalledWith(['/']);
  });

  it('shows the invalid-credentials message on a 401 and re-enables Sign in', async () => {
    login.mockRejectedValue(
      new HttpErrorResponse({
        status: 401,
        error: { type: '/problems/invalid-credentials' },
      }),
    );
    const { fixture, el, submit, fill } = setup();
    await fixture.whenStable();
    await fill('staff@demo.test', 'wrong');
    submit().click();
    await fixture.whenStable();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      PROBLEM_MESSAGES['invalid-credentials'],
    );
    expect(el.querySelector('input[type="email"]')).toHaveProperty('value', 'staff@demo.test');
    expect(submit().disabled).toBe(true); // password was cleared, so the form is invalid again
  });
});
