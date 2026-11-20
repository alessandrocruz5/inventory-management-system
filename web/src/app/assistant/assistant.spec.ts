import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Assistant } from './assistant';
import { AssistantService } from './assistant.service';
import { signal } from '@angular/core';

describe('Assistant', () => {
  const status = vi.fn();
  const send = vi.fn();
  const clear = vi.fn();
  const conversation = signal<unknown[]>([]);

  beforeEach(async () => {
    status.mockReset();
    send.mockReset();
    clear.mockReset();
    conversation.set([]);
    status.mockResolvedValue({ configured: true, model: 'm' });
    send.mockImplementation(async (text: string) => {
      conversation.update((c) => [...c, { role: 'user', content: text }]);
    });
    await TestBed.configureTestingModule({
      imports: [Assistant],
      providers: [
        {
          provide: AssistantService,
          useValue: { status, send, clear, conversation: conversation.asReadonly() },
        },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(Assistant);
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    return { fixture, el };
  }

  const textarea = (el: HTMLElement) => el.querySelector('textarea') as HTMLTextAreaElement;
  const sendButton = (el: HTMLElement) =>
    Array.from(el.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Send',
    ) as HTMLButtonElement;

  async function type(fixture: Awaited<ReturnType<typeof render>>['fixture'], value: string) {
    const ta = textarea(fixture.nativeElement);
    ta.value = value;
    ta.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('shows the not-configured banner and disables the input', async () => {
    status.mockResolvedValue({ configured: false, model: null });
    const { el } = await render();
    expect(el.querySelector('.banner')?.textContent).toContain("isn't set up");
    expect(textarea(el).disabled).toBe(true);
    expect(sendButton(el).disabled).toBe(true);
  });

  it('shows the empty state and fills the input from a suggestion', async () => {
    const { fixture, el } = await render();
    expect(el.textContent).toContain('Ask about your stock');
    const first = el.querySelector('.suggestions button') as HTMLButtonElement;
    first.click();
    fixture.detectChanges();
    expect(textarea(el).value).toBe('How much stock do we have at each location?');
  });

  it('renders reply text literally, with no HTML elements', async () => {
    const { fixture, el } = await render();
    conversation.set([{ role: 'assistant', content: '<b>bold</b>', toolCalls: [], drafts: [] }]);
    fixture.detectChanges();
    expect(el.querySelector('.bubble')?.textContent).toBe('<b>bold</b>');
    expect(el.querySelector('.bubble b')).toBeNull();
  });

  it('labels tool chips and marks failed calls', async () => {
    const { fixture, el } = await render();
    conversation.set([
      {
        role: 'assistant',
        content: 'x',
        toolCalls: [
          { name: 'get_stock', arguments: null, succeeded: true },
          { name: 'draft_document', arguments: null, succeeded: false },
          { name: 'mystery', arguments: null, succeeded: true },
        ],
        drafts: [],
      },
    ]);
    fixture.detectChanges();
    const chips = Array.from(el.querySelectorAll('.chip'));
    expect(chips.map((c) => c.textContent?.trim())).toEqual([
      'Checked stock',
      'Drafted a document',
      'mystery',
    ]);
    expect(chips.map((c) => c.classList.contains('failed'))).toEqual([false, true, false]);
  });

  it('sends on click and clears the input', async () => {
    const { fixture, el } = await render();
    await type(fixture, 'hello');
    sendButton(el).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(send).toHaveBeenCalledWith('hello');
    expect(textarea(el).value).toBe('');
  });

  it('shows the mapped message and restores the input on a 503', async () => {
    send.mockRejectedValue(
      new HttpErrorResponse({
        status: 503,
        error: { type: '/problems/assistant-unavailable' },
      }),
    );
    const { fixture, el } = await render();
    await type(fixture, 'hello');
    sendButton(el).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('.error')?.textContent).toContain("model didn't respond");
    expect(textarea(el).value).toBe('hello');
  });

  it('Enter sends and Shift+Enter does not', async () => {
    const { fixture, el } = await render();
    await type(fixture, 'hello');
    textarea(el).dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', shiftKey: true, cancelable: true }),
    );
    await fixture.whenStable();
    expect(send).not.toHaveBeenCalled();
    const enter = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
    textarea(el).dispatchEvent(enter);
    await fixture.whenStable();
    expect(enter.defaultPrevented).toBe(true);
    expect(send).toHaveBeenCalledWith('hello');
  });

  it('shows the character counter', async () => {
    const { fixture, el } = await render();
    await type(fixture, 'x'.repeat(123));
    expect(el.querySelector('.counter')?.textContent?.trim()).toBe('123 / 2,000');
  });

  it('clears the conversation', async () => {
    const { fixture, el } = await render();
    conversation.set([{ role: 'user', content: 'hi' }]);
    fixture.detectChanges();
    const btn = Array.from(el.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Clear conversation'),
    ) as HTMLButtonElement;
    btn.click();
    expect(clear).toHaveBeenCalled();
  });
});
