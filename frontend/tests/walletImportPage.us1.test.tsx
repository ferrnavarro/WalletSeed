import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import WalletImportPage from '../src/pages/WalletImportPage';
import * as walletClient from '../src/api/walletClient';

vi.mock('../src/api/walletClient', () => ({
  listAccounts: vi.fn(),
  listCategories: vi.fn(),
  compare: vi.fn(),
}));

describe('WalletImportPage compare flow', () => {
  beforeEach(() => {
    vi.mocked(walletClient.listAccounts).mockResolvedValue({
      ok: true,
      data: [
        { id: 'acct-1', name: 'Main Checking', currencyCode: 'USD', accountType: 'checking' },
      ],
    });
    vi.mocked(walletClient.listCategories).mockResolvedValue({
      ok: true,
      data: [{ id: 'cat-1', name: 'Food', color: null }],
    });
  });

  it('loads accounts, uploads a file, and renders the comparison view', async () => {
    vi.mocked(walletClient.compare).mockResolvedValue({
      ok: true,
      data: {
        window: {
          from: '2026-06-01',
          to: '2026-06-30',
          issueDate: '2026-06-01',
          cutoffDate: '2026-06-30',
        },
        account: { id: 'acct-1', name: 'Main Checking', currencyCode: 'USD', accountType: 'checking' },
        categories: [],
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
            matchedWalletRecordIds: ['w-1'],
            defaultSelected: false,
            currencyMismatch: false,
            previewLabelIds: [],
            previewLabelNames: [],
          },
        ],
        walletRows: [
          {
            id: 'w-1',
            date: '2026-06-11',
            signedAmount: -12.5,
            currency: 'USD',
            note: 'Coffee',
            counterParty: null,
            categoryName: 'Food',
            claimedByPdfIndices: [0],
          },
        ],
        unmappedSections: ['MAIN'],
      },
    });

    render(<WalletImportPage />);

    await waitFor(() => {
      expect(screen.getByRole('option', { name: /main checking/i })).toBeInTheDocument();
    });

    await userEvent.selectOptions(screen.getByLabelText(/wallet account/i), 'acct-1');

    const file = new File(['dummy pdf'], 'statement.pdf', { type: 'application/pdf' });
    const input = screen.getByLabelText(/choose pdf statement/i);
    await userEvent.upload(input, file);
    await userEvent.click(screen.getByRole('button', { name: /extract statement/i }));

    await waitFor(() => {
      expect(screen.getAllByText('Coffee').length).toBeGreaterThan(0);
    });

    expect(screen.getByText(/Matches W-/i)).toBeInTheDocument();
    expect(screen.getByText(/Claimed by row #/i)).toBeInTheDocument();
  });
});
