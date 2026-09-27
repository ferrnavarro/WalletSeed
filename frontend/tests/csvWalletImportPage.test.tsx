import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import CsvWalletImportPage from '../src/pages/CsvWalletImportPage';
import * as walletClient from '../src/api/walletClient';

vi.mock('../src/api/walletClient', () => ({
  listAccounts: vi.fn(),
  listCategories: vi.fn(),
  compareCsv: vi.fn(),
  submit: vi.fn(),
}));

const csvCompareResponse = {
  window: { from: '2026-07-20', to: '2026-09-02', issueDate: '2026-07-25', cutoffDate: '2026-08-28' },
  account: { id: 'acct-1', name: 'Main Card', currencyCode: 'USD', accountType: 'creditcard' },
  categories: [{ id: 'cat-1', name: 'Food', color: null }],
  pdfRows: [
    {
      index: 0,
      date: '2026-07-25',
      signedAmount: -54.42,
      currency: 'USD',
      description: 'LA PAMPA ARGENTINA PASEO SAN SALVADO',
      counterParty: null,
      cardholderSectionRawName: '',
      cardLast4: '2127',
      matchedWalletRecordIds: [],
      defaultSelected: true,
      currencyMismatch: false,
      previewLabelIds: [],
      previewLabelNames: [],
    },
    {
      index: 1,
      date: '2026-07-29',
      signedAmount: -206.0,
      currency: 'USD',
      description: 'DIDDUS -BP- SAN SALVADO',
      counterParty: null,
      cardholderSectionRawName: '',
      cardLast4: '2127',
      matchedWalletRecordIds: ['w-9'],
      defaultSelected: false,
      currencyMismatch: false,
      previewLabelIds: [],
      previewLabelNames: [],
    },
  ],
  walletRows: [
    {
      id: 'w-9',
      date: '2026-07-29',
      signedAmount: -206.0,
      currency: 'USD',
      note: 'DIDDUS',
      counterParty: null,
      categoryName: 'Food',
      claimedByPdfIndices: [1],
    },
  ],
  unmappedSections: [],
  fileErrors: [],
};

async function renderAndUpload() {
  render(<CsvWalletImportPage />);

  await waitFor(() => {
    expect(screen.getByRole('option', { name: /main card/i })).toBeInTheDocument();
  });

  await userEvent.selectOptions(screen.getByLabelText(/wallet account/i), 'acct-1');

  const file = new File(['dummy csv'], 'estado.csv', { type: 'text/csv' });
  const input = screen.getByLabelText(/choose csv files/i);
  await userEvent.upload(input, file);
  await userEvent.click(screen.getByRole('button', { name: /compare with wallet/i }));

  await waitFor(() => {
    expect(screen.getByText('LA PAMPA ARGENTINA PASEO SAN SALVADO')).toBeInTheDocument();
  });
}

describe('CsvWalletImportPage', () => {
  beforeEach(() => {
    vi.mocked(walletClient.listAccounts).mockResolvedValue({
      ok: true,
      data: [{ id: 'acct-1', name: 'Main Card', currencyCode: 'USD', accountType: 'creditcard' }],
    });
    vi.mocked(walletClient.listCategories).mockResolvedValue({
      ok: true,
      data: [{ id: 'cat-1', name: 'Food', color: null }],
    });
    vi.mocked(walletClient.compareCsv).mockResolvedValue({ ok: true, data: csvCompareResponse });
  });

  it('renders merged comparison rows after upload', async () => {
    await renderAndUpload();

    expect(screen.getByText('DIDDUS -BP- SAN SALVADO')).toBeInTheDocument();
    expect(screen.getByText(/Matches W-/i)).toBeInTheDocument();
    expect(screen.getByText('Selected: 1 of 2')).toBeInTheDocument();
    // Import disabled because selected row (index 0) has no category yet
    expect(screen.getByRole('button', { name: /import to wallet/i })).toBeDisabled();
  });

  it('enables import after selecting a category and submits', async () => {
    vi.mocked(walletClient.submit).mockResolvedValue({
      ok: true,
      data: { outcomes: [{ index: 0, ok: true, walletRecordId: 'w-new', errorMessage: null }] },
    });

    await renderAndUpload();

    await userEvent.selectOptions(screen.getByLabelText('Category for row 0'), 'cat-1');

    const importButton = screen.getByRole('button', { name: /import to wallet/i });
    expect(importButton).toBeEnabled();
    await userEvent.click(importButton);

    await waitFor(() => {
      expect(screen.getByText(/Import outcomes/i)).toBeInTheDocument();
    });
    expect(screen.getByText(/w-new/i)).toBeInTheDocument();

    expect(vi.mocked(walletClient.submit)).toHaveBeenCalledWith({
      accountId: 'acct-1',
      rows: [
        expect.objectContaining({
          index: 0,
          signedAmount: -54.42,
          currency: 'USD',
          categoryId: 'cat-1',
          cardholderSectionRawName: '',
        }),
      ],
    });
  });

  it('shows per-file errors without blocking valid rows', async () => {
    vi.mocked(walletClient.compareCsv).mockResolvedValue({
      ok: true,
      data: { ...csvCompareResponse, fileErrors: [{ fileName: 'broken.csv', message: 'No valid transactions found in the CSV file.' }] },
    });

    await renderAndUpload();

    expect(screen.getByText(/Some files could not be parsed/i)).toBeInTheDocument();
    expect(screen.getByText(/broken.csv/i)).toBeInTheDocument();
    expect(screen.getByText('LA PAMPA ARGENTINA PASEO SAN SALVADO')).toBeInTheDocument();
  });
});
