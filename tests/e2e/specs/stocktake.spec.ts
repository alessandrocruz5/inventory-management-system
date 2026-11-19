import { expect, test } from '@playwright/test';
import { ADMIN, STAFF, choose, createLocation, signIn } from '../support/app';

test.use({ viewport: { width: 375, height: 812 } });

const SKU = 'CANDLE-WHT';

test('T18 · Staff counts, Admin posts, stock shows the count (375 px)', async ({
  page,
  browser,
}) => {
  const loc = await test.step('Admin creates a location and receives 10 @ ₱20', async () => {
    const created = await createLocation(browser);
    const context = await browser.newContext();
    try {
      const admin = await context.newPage();
      await signIn(admin, ADMIN);
      const result = await admin.evaluate(
        async ({ locationId, sku }) => {
          const items = await (await fetch(`/api/items?search=${sku}&pageSize=10`)).json();
          const item = (items.items as { id: string; sku: string }[]).find((i) => i.sku === sku);
          const response = await fetch('/api/documents/receipts', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Idempotency-Key': crypto.randomUUID() },
            body: JSON.stringify({
              locationId,
              reference: 'T18 receipt',
              lines: [{ itemId: item?.id, quantity: 10, unitCost: 20 }],
            }),
          });
          return response.status;
        },
        { locationId: created.id, sku: SKU },
      );
      expect(result).toBe(201);
    } finally {
      await context.close();
    }
    return created;
  });

  const stocktakeUrl = await test.step('Staff starts a count and counts 8', async () => {
    await signIn(page, STAFF);
    await page.goto('/stocktakes');
    await page.getByRole('button', { name: 'Start a count' }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('combobox', { name: 'Location' }).click();
    await page.getByRole('option', { name: new RegExp(`^${loc.code} ·`) }).click();
    await dialog.getByRole('button', { name: 'Start counting' }).click();
    await expect(page).toHaveURL(/\/stocktakes\/[0-9a-f-]{36}$/);

    const line = page.getByTestId('line').filter({ hasText: SKU });
    await expect(line.getByTestId('counted')).toHaveText('Not counted');
    await line.click();
    await page.getByTestId('count-quantity').fill('8');
    await page.getByRole('button', { name: 'Save count' }).click();
    await expect(line.getByTestId('expected')).toContainText('Expected 10');
    await expect(line.getByTestId('difference')).toHaveText(/[−-]2/);
    return page.url();
  });

  await test.step('Staff has no Post count', async () => {
    await expect(page.getByRole('button', { name: 'Post count' })).toHaveCount(0);
  });

  const adminContext = await browser.newContext();
  try {
    const admin = await adminContext.newPage();

    await test.step('Admin posts the count', async () => {
      await signIn(admin, ADMIN);
      await admin.goto(stocktakeUrl);
      await admin.getByRole('button', { name: 'Post count' }).click();
      await admin.getByRole('dialog').getByRole('button', { name: 'Post count' }).click();
      await expect(admin.getByTestId('posted-notice')).toBeVisible();
    });

    await test.step('The correction moves 2 out', async () => {
      await admin.getByRole('link', { name: 'View the correction' }).click();
      await expect(admin).toHaveURL(/\/documents\/[0-9a-f-]{36}$/);
      await expect(admin.getByRole('heading', { name: 'Corrected' })).toBeVisible();
      // A shrink is a movement of 2 out of the location (₱20 each), shown unsigned.
      const row = admin.getByRole('row').filter({ hasText: SKU });
      await expect(row).toHaveCount(1);
      await expect(row).toContainText(`${loc.code} →`);
      await expect(row).toContainText(/\b2 /);
      await expect(row).toContainText('₱40.00');
    });

    await test.step('Stock shows 8 and ₱160.00', async () => {
      // At 375 px /stock is a stacked list, not the grid.
      await admin.goto('/stock');
      await choose(admin, 'Location', new RegExp(`^${loc.code} ·`));
      const row = admin.getByRole('link').filter({ hasText: SKU });
      await expect(row).toHaveCount(1);
      await expect(row.locator('.row-qty')).toContainText('8');
      await expect(row.locator('.row-value')).toHaveText('₱160.00');
    });
  } finally {
    await adminContext.close();
  }
});
