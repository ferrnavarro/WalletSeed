import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import WalletImportPage from '../src/pages/WalletImportPage';
import * as walletClient from '../src/api/walletClient';

vi.mock('../src/api/walletClient', () => ({
  listAccounts: vi.fn(),
  listCategories: vi.fn(),
  compare: vi.fn(),
  submit: vi.fn(),
}));

describe('WalletImportPage submit flow', () => {
  beforeEach(() => {
    vi.mocked(walletClient.listAccounts).mockResolvedValue({
      ok: true,
      data: [{ id: 'acct-1', name: 'Main Checking', currencyCode: 'USD', accountType: 'checking' }],
    });
    vi.mocked(walletClient.listCategories).mockResolvedValue({
      ok: true,
      data: [{ id: 'cat-1', name: 'Food', color: null }],
    });
  });

  it('lets the user stage a row, assign a category, and submit it', async () => {
    vi.mocked(walletClient.compare).mockResolvedValue({
      ok: true,
      data: {
        window: { from: '2026-06-01', to: '2026-06-30', issueDate: '2026-06-01', cutoffDate: '2026-06-30' },
        account: { id: 'acct-1', name: 'Main Checking', currencyCode: 'USD', accountType: 'checking' },
        categories: [{ id: 'cat-1', name: 'Food', color: null }],
        pdfRows: [
          {
            index: 0,
            date: '2026-06-10',
            signedAmount: -12.5,
            currency: 'USD',
            description: 'Coffee',
            counterParty: null,
            cardholderSectionRawName: 'MAIN',
            cardLast4: '1234',
            matchedWalletRecordIds: [],
            defaultSelected: true,
            currencyMismatch: false,
            previewLabelIds: [],
            previewLabelNames: [],
          },
        ],
        walletRows: [],
        unmappedSections: ['MAIN'],
      },
    });
    vi.mocked(walletClient.submit).mockResolvedValue({
      ok: true,
      data: { outcomes: [{ index: 0, ok: true, walletRecordId: 'w-1', errorMessage: null }] },
    });

    render(<WalletImportPage />);

    await waitFor(() => {
      expect(screen.getByRole('option', { name: /main checking/i })).toBeInTheDocument();
    });

    await userEvent.selectOptions(screen.getByLabelText(/wallet account/i), 'acct-1');

    const file = new File(['dummy pdf'], 'statement.pdf', { type: 'application/pdf' });
    await userEvent.upload(screen.getByLabelText(/choose pdf statement/i), file);
    await userEvent.click(screen.getByRole('button', { name: /extract statement/i }));

    await waitFor(() => {
      expect(screen.getByText('Coffee')).toBeInTheDocument();
    });

    await userEvent.selectOptions(screen.getByLabelText(/category for row 0/i), 'cat-1');
    await userEvent.click(screen.getByRole('button', { name: /import to wallet/i }));

    await waitFor(() => {
      expect(screen.getByText(/w-1/i)).toBeInTheDocument();
    });
  });
});
