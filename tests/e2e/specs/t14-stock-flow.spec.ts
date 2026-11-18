import { expect, test } from '@playwright/test';
import { ADMIN, STAFF, createLocation, postDocument, signIn, stockRow } from '../support/app';

const SKU = 'CANDLE-WHT';

test('T14 · Staff receives 10 @ ₱100 and issues 3; Admin reverses the issue', async ({
  page,
  browser,
}) => {
  const loc = await test.step('Admin creates a fresh location', () => createLocation(browser));

  await test.step('Staff signs in and receives 10 @ ₱100', async () => {
    await signIn(page, STAFF);
    await postDocument(page, 'receipt', {
      locationCode: loc.code,
      sku: SKU,
      quantity: 10,
      unitCost: 100,
      reference: 'T14 receipt',
    });
  });

  const issueId = await test.step('Staff issues 3 and has no Reverse button', async () => {
    const id = await postDocument(page, 'issue', {
      locationCode: loc.code,
      sku: SKU,
      quantity: 3,
      reference: 'T14 issue',
    });
    await expect(page.getByRole('heading').first()).toBeVisible();
    await expect(page.getByRole('button', { name: 'Reverse' })).toHaveCount(0);
    return id;
  });

  await test.step('Staff sees 7, ₱100.00 and ₱700.00', async () => {
    const row = await stockRow(page, loc.code, SKU);
    await expect(row.locator('[col-id="onHand"]')).toHaveText('7');
    await expect(row.locator('[col-id="averageCost"]')).toHaveText('₱100.00');
    await expect(row.locator('[col-id="stockValue"]')).toHaveText('₱700.00');
  });

  const adminContext = await browser.newContext();
  try {
    const admin = await adminContext.newPage();

    await test.step('Admin reverses the issue', async () => {
      await signIn(admin, ADMIN);
      await admin.goto(`/documents/${issueId}`);
      await admin.getByRole('button', { name: 'Reverse' }).click();
      await admin.getByRole('dialog').getByRole('button', { name: 'Reverse' }).click();
      await expect(admin.getByText('This posting was reversed.')).toBeVisible();
    });

    await test.step('Admin sees 10, ₱100.00 and ₱1,000.00', async () => {
      const row = await stockRow(admin, loc.code, SKU);
      await expect(row.locator('[col-id="onHand"]')).toHaveText('10');
      await expect(row.locator('[col-id="averageCost"]')).toHaveText('₱100.00');
      await expect(row.locator('[col-id="stockValue"]')).toHaveText('₱1,000.00');
    });
  } finally {
    await adminContext.close();
  }
});
