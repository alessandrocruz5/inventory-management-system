import { expect, test } from '@playwright/test';
import { ADMIN, STAFF, signIn } from '../support/app';

test.describe('Ledger health page', () => {
  test('Admin sees INV-1 to INV-5 pass, and a rebuild finds nothing to fix', async ({ page }) => {
    await signIn(page, ADMIN);
    await page.getByRole('link', { name: 'Ledger health' }).click();
    await expect(page).toHaveURL(/\/admin\/ledger$/);

    await expect(page.getByText('All checks passed')).toBeVisible();
    await expect(page.getByText('Passed', { exact: true })).toHaveCount(5);

    await page.getByRole('button', { name: 'Rebuild balances' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'Rebuild' }).click();
    await expect(page.getByText(/Nothing to fix\./)).toBeVisible();
  });

  test('Staff has no Ledger health link and is sent to /stock', async ({ page }) => {
    await signIn(page, STAFF);
    await expect(page.getByRole('link', { name: 'Ledger health' })).toHaveCount(0);

    await page.goto('/admin/ledger');
    await expect(page).toHaveURL(/\/stock$/);
  });
});
