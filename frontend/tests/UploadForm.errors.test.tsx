import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import UploadForm from '../src/components/UploadForm';

describe('UploadForm Errors', () => {
  it('triggers onLocalError with INVALID_FILE_TYPE when a non-PDF is uploaded', async () => {
    const handleSubmit = vi.fn();
    const handleLocalError = vi.fn();

    render(<UploadForm onSubmit={handleSubmit} onLocalError={handleLocalError} />);

    const file = new File(['dummy content'], 'statement.txt', { type: 'text/plain' });
    const input = screen.getByLabelText(/choose pdf statement.../i);

    // Remove accept filter to simulate browser bypass / drag-and-drop
    input.removeAttribute('accept');

    await userEvent.upload(input, file);

    const submitBtn = screen.getByRole('button', { name: /extract statement/i });
    expect(submitBtn).toBeEnabled();

    await userEvent.click(submitBtn);

    expect(handleLocalError).toHaveBeenCalledWith({
      code: 'INVALID_FILE_TYPE',
      message: 'Please upload a PDF file.'
    });
    expect(handleSubmit).not.toHaveBeenCalled();
  });

  it('triggers onLocalError with FILE_TOO_LARGE when file is over 25MB', async () => {
    const handleSubmit = vi.fn();
    const handleLocalError = vi.fn();

    render(<UploadForm onSubmit={handleSubmit} onLocalError={handleLocalError} />);

    // Create a 26 MB file
    const largeFile = new File([new ArrayBuffer(26 * 1024 * 1024)], 'large_statement.pdf', { type: 'application/pdf' });
    const input = screen.getByLabelText(/choose pdf statement.../i);

    await userEvent.upload(input, largeFile);

    const submitBtn = screen.getByRole('button', { name: /extract statement/i });
    expect(submitBtn).toBeEnabled();

    await userEvent.click(submitBtn);

    expect(handleLocalError).toHaveBeenCalledWith({
      code: 'FILE_TOO_LARGE',
      message: 'This file exceeds the 25 MB limit.'
    });
    expect(handleSubmit).not.toHaveBeenCalled();
  });
});
