import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import App from '../src/App';
import * as client from '../src/api/statementsClient';

vi.mock('../src/api/statementsClient', () => ({
  extractStatement: vi.fn(),
}));

const ERROR_TEST_CASES = [
  {
    code: 'INVALID_FILE_TYPE',
    expectedText: 'Please upload a PDF file.',
    status: 400
  },
  {
    code: 'EMPTY_FILE',
    expectedText: 'The selected file is empty.',
    status: 400
  },
  {
    code: 'FILE_TOO_LARGE',
    expectedText: 'This file exceeds the 25 MB limit.',
    status: 413
  },
  {
    code: 'PASSWORD_PROTECTED',
    expectedText: 'This PDF is password-protected. Please remove the password and try again.',
    status: 422
  },
  {
    code: 'NO_TEXT_EXTRACTABLE',
    expectedText: "This PDF doesn't contain machine-readable text. Scanned PDFs aren't supported in this version.",
    status: 422
  },
  {
    code: 'UNRECOGNIZED_LAYOUT',
    expectedText: "We couldn't recognize this as a BAC Credomatic statement.",
    status: 422
  },
  {
    code: 'PARSE_FAILED',
    expectedText: 'Something went wrong while reading this PDF. Please try again.',
    status: 500
  }
] as const;

describe('App Errors Integration Flow', () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  ERROR_TEST_CASES.forEach(({ code, expectedText, status }) => {
    it(`handles API error code ${code} (status ${status}) and renders the correct banner`, async () => {
      vi.mocked(client.extractStatement).mockResolvedValue({
        ok: false,
        error: { code, message: `Original error message for ${code}` },
        httpStatus: status
      });

      render(<App />);

      const file = new File(['dummy content'], 'statement.pdf', { type: 'application/pdf' });
      const input = screen.getByLabelText(/choose pdf statement.../i);

      await userEvent.upload(input, file);
      const submitBtn = screen.getByRole('button', { name: /extract statement/i });
      await userEvent.click(submitBtn);

      // Wait for error banner to render
      await waitFor(() => {
        expect(screen.getByText(expectedText)).toBeInTheDocument();
      });

      // Verify that the error code and status are also present in the meta info
      expect(screen.getByText(`Code: ${code} | HTTP ${status}`)).toBeInTheDocument();

      // Assert that the UploadForm is still present and enabled
      expect(screen.getByText('Upload Credit Card Statement')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /extract statement/i })).toBeInTheDocument();
    });
  });
});
