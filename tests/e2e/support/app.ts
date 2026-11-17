import { expect, type Browser, type Locator, type Page } from '@playwright/test';

export const STAFF = 'staff@demo.test';
export const ADMIN = 'admin@demo.test';

/** The AppHost `seed-password`. Read lazily so `playwright test --list` works without it. */
export function password(): string {
  const value = process.env['E2E_PASSWORD'];
  if (!value) {
    throw new Error(
      'E2E_PASSWORD is not set: export the AppHost seed-password (README "End-to-end tests")',
    );
  }
  return value;
}

export async function signIn(page: Page, email: string): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password());
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page).toHaveURL(/\/stock$/);
}

/**
 * Creates a fresh physical location as Admin. The request is an in-browser `fetch` from a signed-in
 * page, so the `Secure` + `SameSite=Strict` session cookie behaves exactly as it does for the app.
 * The code sorts newest-first (`MAX_SAFE_INTEGER - now`), so reruns never fall off a 100-row list.
 */
export async function createLocation(browser: Browser): Promise<{ id: string; code: string }> {
  const context = await browser.newContext();
  try {
    const page = await context.newPage();
    await signIn(page, ADMIN);

    const stamp = (Number.MAX_SAFE_INTEGER - Date.now()).toString(36).toUpperCase();
    const salt = Math.random().toString(36).slice(2, 4).toUpperCase().padEnd(2, '0');
    const code = `E2E-${stamp}${salt}`;
    const body = JSON.stringify({ code, name: `E2E ${new Date().toISOString()}` });

    const result = await page.evaluate(async (payload) => {
      const response = await fetch('/api/locations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: payload,
      });
      return { status: response.status, json: await response.json() };
    }, body);

    expect(result.status).toBe(201);
    return { id: result.json.id as string, code };
  } finally {
    await context.close();
  }
}

/** Opens a combobox by its label and picks the option whose text matches. */
export async function choose(
  page: Page,
  label: string,
  optionText: string | RegExp,
): Promise<void> {
  await page.getByRole('combobox', { name: label }).click();
  await page.getByRole('option', { name: optionText }).click();
}

export interface PostLine {
  locationCode: string;
  sku: string;
  quantity: number;
  unitCost?: number;
  reference: string;
}

/** Posts a one-line receipt or issue through the form; returns the new document's id. */
export async function postDocument(
  page: Page,
  type: 'receipt' | 'issue',
  line: PostLine,
): Promise<string> {
  await page.goto(`/documents/new/${type}`);
  await choose(page, 'Location', new RegExp(`^${line.locationCode} ·`));
  await page.getByLabel('Reference').fill(line.reference);

  await page.getByRole('combobox', { name: 'Item' }).fill(line.sku);
  await page.getByRole('option', { name: new RegExp(`^${line.sku} ·`) }).click();

  await page.getByLabel('Quantity').fill(String(line.quantity));
  if (line.unitCost !== undefined) {
    await page.getByLabel('Unit cost').fill(String(line.unitCost));
  }
  await page.getByRole('button', { name: 'Post' }).click();

  await expect(page).toHaveURL(/\/documents\/[0-9a-f-]{36}$/);
  return new URL(page.url()).pathname.split('/').pop() as string;
}

/** Filters `/stock` to one location and returns the single grid row for the SKU. */
export async function stockRow(page: Page, locationCode: string, sku: string): Promise<Locator> {
  await page.goto('/stock');
  await choose(page, 'Location', new RegExp(`^${locationCode} ·`));
  const row = page.getByRole('row').filter({ hasText: sku });
  await expect(row).toHaveCount(1);
  return row;
}
