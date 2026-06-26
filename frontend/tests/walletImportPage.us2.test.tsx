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

describe('WalletImportPage selection and preview flow', () => {
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

  it('keeps selection state, enables submit only when categories are present, and shows the preview', async () => {
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
            defaultSelected: false,
            currencyMismatch: false,
            previewLabelIds: ['lbl-1'],
            previewLabelNames: ['Coffee labels'],
          },
        ],
        walletRows: [],
        unmappedSections: [],
      },
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

    const selectToggle = screen.getByLabelText(/select/i);
    expect(selectToggle).not.toBeChecked();
    await userEvent.click(selectToggle);
    expect(selectToggle).toBeChecked();

    const submitButton = screen.getByRole('button', { name: /import to wallet/i });
    expect(submitButton).toBeDisabled();

    await userEvent.selectOptions(screen.getByLabelText(/category for row 0/i), 'cat-1');
    expect(submitButton).toBeEnabled();
    expect(screen.getByText('categoryId → Food')).toBeInTheDocument();
    expect(screen.getByText('labelIds → Coffee labels')).toBeInTheDocument();
  });

  it('allows selecting all rows per card and collapsing sections', async () => {
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
            defaultSelected: false,
            currencyMismatch: false,
            previewLabelIds: [],
            previewLabelNames: [],
          },
          {
            index: 1,
            date: '2026-06-11',
            signedAmount: -20.0,
            currency: 'USD',
            description: 'Lunch',
            counterParty: null,
            cardholderSectionRawName: 'MAIN',
            cardLast4: '1234',
            matchedWalletRecordIds: [],
            defaultSelected: false,
            currencyMismatch: false,
            previewLabelIds: [],
            previewLabelNames: [],
          },
        ],
        walletRows: [],
        unmappedSections: [],
      },
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
      expect(screen.getByText('Lunch')).toBeInTheDocument();
    });

    // Check that we can collapse and expand the card section
    expect(screen.queryByRole('table')).toBeInTheDocument();
    const sectionHeader = screen.getByText('Card last 4: 1234');
    
    // Click header to collapse
    await userEvent.click(sectionHeader);
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    // Click header again to expand
    await userEvent.click(sectionHeader);
    expect(screen.queryByRole('table')).toBeInTheDocument();

    // Test Select All checkbox
    const selectAllCheckbox = screen.getByLabelText('Toggle all rows');
    expect(selectAllCheckbox).not.toBeChecked();

    const rowCheckboxes = screen.getAllByLabelText('Select');
    expect(rowCheckboxes[0]).not.toBeChecked();
    expect(rowCheckboxes[1]).not.toBeChecked();

    // Click Select All
    await userEvent.click(selectAllCheckbox);
    expect(selectAllCheckbox).toBeChecked();
    expect(rowCheckboxes[0]).toBeChecked();
    expect(rowCheckboxes[1]).toBeChecked();

    // Click Select All again to deselect
    await userEvent.click(selectAllCheckbox);
    expect(selectAllCheckbox).not.toBeChecked();
    expect(rowCheckboxes[0]).not.toBeChecked();
    expect(rowCheckboxes[1]).not.toBeChecked();
  });
});
