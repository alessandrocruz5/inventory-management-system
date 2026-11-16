import { expect, test, type Page } from '@playwright/test';
import { STAFF, createLocation, postDocument, signIn } from '../support/app';

test.use({ viewport: { width: 375, height: 812 } });

async function expectNoSidewaysScroll(page: Page): Promise<void> {
  const width = await page.evaluate(() => document.documentElement.scrollWidth);
  expect(width).toBeLessThanOrEqual(375);
}

test('Post-stock form works at 375 px: no sideways scroll, receipt posts', async ({
  page,
  browser,
}) => {
  const loc = await createLocation(browser);

  await signIn(page, STAFF);
  await page.goto('/documents/new/receipt');
  await expect(page.getByRole('button', { name: 'Post' })).toBeVisible();
  await expectNoSidewaysScroll(page);

  await postDocument(page, 'receipt', {
    locationCode: loc.code,
    sku: 'CANDLE-WHT',
    quantity: 1,
    unitCost: 1,
    reference: 'E2E 375px',
  });
  await expect(page.getByRole('heading').first()).toBeVisible();
  await expectNoSidewaysScroll(page);
});
