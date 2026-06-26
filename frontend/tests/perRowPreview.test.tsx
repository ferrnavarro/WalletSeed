import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import PerRowPreview from '../src/components/PerRowPreview';

describe('PerRowPreview', () => {
  it('renders the payload preview fields and the no-label fallback', () => {
    render(
      <PerRowPreview
        row={{
          index: 1,
          date: '2026-06-10',
          signedAmount: -12.5,
          currency: 'USD',
          description: 'Coffee',
          counterParty: 'Cafe',
          cardholderSectionRawName: 'MAIN',
          cardLast4: '1234',
          matchedWalletRecordIds: [],
          defaultSelected: true,
          currencyMismatch: false,
          previewLabelIds: [],
          previewLabelNames: [],
        }}
        accountName="Main Checking"
        categoryName="Food"
      />,
    );

    expect(screen.getByText('accountId → Main Checking')).toBeInTheDocument();
    expect(screen.getByText('recordDate → 2026-06-10')).toBeInTheDocument();
    expect(screen.getByText('amount.value → -12.50')).toBeInTheDocument();
    expect(screen.getByText('paymentType → credit_card')).toBeInTheDocument();
    expect(screen.getByText('categoryId → Food')).toBeInTheDocument();
    expect(screen.getByText('labelIds → (no label)')).toBeInTheDocument();
    expect(screen.getByText('note → Coffee')).toBeInTheDocument();
    expect(screen.getByText('counterParty → Cafe')).toBeInTheDocument();
  });
});
