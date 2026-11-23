import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService, CurrentUser } from '../core/auth/auth.service';
import { AssistantService, MAX_MESSAGES, MAX_TOTAL_CHARS } from './assistant.service';

const reply = (text: string) => ({
  reply: text,
  toolCalls: [],
  drafts: [],
  usage: { inputTokens: 1, outputTokens: 1, modelCalls: 1 },
  model: 'm',
});

describe('AssistantService', () => {
  let service: AssistantService;
  let httpMock: HttpTestingController;
  const user = signal<CurrentUser | null>({
    id: 'u1',
    email: 'a@b.c',
    role: 'Staff',
    tenantName: 'T',
  });

  beforeEach(() => {
    user.set({ id: 'u1', email: 'a@b.c', role: 'Staff', tenantName: 'T' });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { user } },
      ],
    });
    service = TestBed.inject(AssistantService);
    httpMock = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  afterEach(() => httpMock.verify());

  async function sendOk(text: string, answer = 'ok') {
    const promise = service.send(text);
    httpMock.expectOne('/api/assistant/chat').flush(reply(answer));
    await promise;
  }

  it('status gets the status endpoint', async () => {
    const promise = service.status();
    const req = httpMock.expectOne('/api/assistant/status');
    expect(req.request.method).toBe('GET');
    req.flush({ configured: true, model: 'm' });
    expect(await promise).toEqual({ configured: true, model: 'm' });
  });

  it('send posts role and content only, and appends the reply', async () => {
    const promise = service.send('  hello  ');
    const req = httpMock.expectOne('/api/assistant/chat');
    expect(req.request.body).toEqual({ messages: [{ role: 'user', content: 'hello' }] });
    req.flush(reply('hi'));
    await promise;
    expect(service.conversation().map((e) => e.content)).toEqual(['hello', 'hi']);
  });

  it('rejects blank and over-long messages locally', async () => {
    await expect(service.send('   ')).rejects.toThrow();
    await expect(service.send('x'.repeat(2001))).rejects.toThrow();
    expect(service.conversation()).toEqual([]);
  });

  it('trims the history to 20 messages', async () => {
    for (let i = 0; i < 12; i++) {
      await sendOk(`q${i}`, `a${i}`);
    }
    const promise = service.send('last');
    const req = httpMock.expectOne('/api/assistant/chat');
    expect(req.request.body.messages).toHaveLength(MAX_MESSAGES);
    expect(req.request.body.messages.at(-1)).toEqual({ role: 'user', content: 'last' });
    req.flush(reply('ok'));
    await promise;
  });

  it('trims the history to 24,000 characters, dropping the oldest', async () => {
    for (let i = 0; i < 3; i++) {
      await sendOk('q', 'a'.repeat(8000));
    }
    const promise = service.send('last');
    const req = httpMock.expectOne('/api/assistant/chat');
    const messages = req.request.body.messages as { content: string }[];
    const total = messages.reduce((n, m) => n + m.content.length, 0);
    expect(total).toBeLessThanOrEqual(MAX_TOTAL_CHARS);
    expect(messages.at(-1)?.content).toBe('last');
    req.flush(reply('ok'));
    await promise;
  });

  it('removes the user entry and rethrows on error', async () => {
    const promise = service.send('hello');
    httpMock
      .expectOne('/api/assistant/chat')
      .flush({ type: 'x' }, { status: 503, statusText: 'Unavailable' });
    await expect(promise).rejects.toBeTruthy();
    expect(service.conversation()).toEqual([]);
  });

  it('clears the conversation when the signed-in user changes', async () => {
    await sendOk('hello');
    expect(service.conversation()).toHaveLength(2);
    user.set({ id: 'u2', email: 'x@y.z', role: 'Staff', tenantName: 'T' });
    TestBed.tick();
    expect(service.conversation()).toEqual([]);
  });
});
