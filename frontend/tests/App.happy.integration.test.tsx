import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import App from '../src/App';
import * as client from '../src/api/statementsClient';
import { ExtractedStatementResponse } from '../src/types/api';

vi.mock('../src/api/statementsClient', () => ({
  extractStatement: vi.fn(),
}));

const mockResponse: ExtractedStatementResponse = {
  statement: {
    cardType: 'VISA INFINITE BLACK',
    maskedAccount: '4593-78XX-XXXX-2145',
    period: {
      issueDate: '2026-05-21',
      cutoffDate: '2026-05-18',
    },
    pageCount: 5,
  },
  sections: [
    {
      cardLast4: '2533',
      rawName: 'MAMÁ',
      transactions: [
        {
          date: '2026-04-18',
          postingDate: '2026-04-19',
          referenceNumber: '000123',
          sequenceCode: 'C011',
          rowType: 'purchase',
          description: 'BURGER KING AHUACHAPAN',
          amount: 2.0,
          direction: 'expense',
          cardLast4: '2533',
          needsReview: false,
          categoryId: null,
          categoryName: null,
          labelId: null,
          labelName: null,
          labelUnmapped: false,
        },
      ],
      totals: {
        computedCharges: 2.0,
        computedCredits: 0.0,
        printedCharges: 2.0,
        printedCredits: 0.0,
      },
      reconciliationStatus: 'ok',
    },
    {
      cardLast4: '2640',
      rawName: 'FÁTIMA',
      transactions: [
        {
          date: '2026-04-17',
          postingDate: '2026-04-18',
          referenceNumber: '000456',
          sequenceCode: 'C012',
          rowType: 'purchase',
          description: 'BELLE AND BARK SAN S',
          amount: 22.0,
          direction: 'expense',
          cardLast4: '2640',
          needsReview: false,
          categoryId: null,
          categoryName: null,
          labelId: null,
          labelName: null,
          labelUnmapped: false,
        },
      ],
      totals: {
        computedCharges: 22.0,
        computedCredits: 0.0,
        printedCharges: 22.0,
        printedCredits: 0.0,
      },
      reconciliationStatus: 'ok',
    },
  ],
  totals: {
    computedExpense: 24.0,
    computedIncome: 0.0,
    printedExpense: 24.0,
    printedIncome: 0.0,
  },
  reconciliationStatus: 'ok',
  needsReviewCount: 0,
  unmappedCards: [],
};

describe('App Happy Integration Flow', () => {
  it('drives the upload flow and displays statement contents successfully', async () => {
    vi.mocked(client.extractStatement).mockResolvedValue({
      ok: true,
      data: mockResponse,
    });

    render(<App />);

    // Renders the idle upload form
    expect(screen.getByText('Upload Credit Card Statement')).toBeInTheDocument();

    const file = new File(['dummy bytes'], 'statement.pdf', { type: 'application/pdf' });
    const input = screen.getByLabelText(/choose pdf statement.../i);

    // Upload and submit
    await userEvent.upload(input, file);
    const submitBtn = screen.getByRole('button', { name: /extract statement/i });
    await userEvent.click(submitBtn);

    // Wait for success view
    await waitFor(() => {
      expect(screen.getByText('4593-78XX-XXXX-2145')).toBeInTheDocument();
    });

    // Check header metadata
    expect(screen.getByText('VISA INFINITE BLACK')).toBeInTheDocument();
    expect(screen.getByText('5 pages')).toBeInTheDocument();

    // Check sections and transactions
    expect(screen.getByText('Card last 4: 2533')).toBeInTheDocument();
    expect(screen.getByText('MAMÁ')).toBeInTheDocument();
    expect(screen.getByText('BURGER KING AHUACHAPAN')).toBeInTheDocument();

    expect(screen.getByText('Card last 4: 2640')).toBeInTheDocument();
    expect(screen.getByText('FÁTIMA')).toBeInTheDocument();
    expect(screen.getByText('BELLE AND BARK SAN S')).toBeInTheDocument();

    // Back button exists
    const backBtn = screen.getByRole('button', { name: /upload another statement/i });
    expect(backBtn).toBeInTheDocument();

    // Click back resets the app to idle state
    await userEvent.click(backBtn);
    expect(screen.getByText('Upload Credit Card Statement')).toBeInTheDocument();
  });
});
