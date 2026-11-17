import { TestBed } from '@angular/core/testing';
import { StatusPill } from './status-pill';

describe('StatusPill', () => {
  async function render(label: string, tone?: 'neutral' | 'good' | 'warn' | 'bad') {
    const fixture = TestBed.createComponent(StatusPill);
    fixture.componentRef.setInput('label', label);
    if (tone) {
      fixture.componentRef.setInput('tone', tone);
    }
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the label as the only text of the host', async () => {
    const el = await render('Passed', 'good');
    expect(el.textContent?.trim()).toBe('Passed');
  });

  it('applies the tone', async () => {
    const el = await render('Reversed', 'warn');
    expect(el.getAttribute('data-tone')).toBe('warn');
    expect(el.classList.contains('pill')).toBe(true);
    expect(el.classList.contains('pill--warn')).toBe(true);
  });

  it('defaults to neutral', async () => {
    const el = await render('Out of stock');
    expect(el.getAttribute('data-tone')).toBe('neutral');
    expect(el.classList.contains('pill--neutral')).toBe(true);
  });
});
