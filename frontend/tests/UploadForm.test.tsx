import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import UploadForm from '../src/components/UploadForm';

describe('UploadForm', () => {
  it('renders input and disabled submit button initially', () => {
    const handleSubmit = vi.fn();
    render(<UploadForm onSubmit={handleSubmit} />);

    expect(screen.getByText('Upload Credit Card Statement')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /extract statement/i })).toBeDisabled();
  });

  it('enables submit button when a file is selected and calls onSubmit on submit', async () => {
    const handleSubmit = vi.fn();
    render(<UploadForm onSubmit={handleSubmit} />);

    const file = new File(['dummy content'], 'statement.pdf', { type: 'application/pdf' });
    const input = screen.getByLabelText(/choose pdf statement.../i);

    // Select file
    await userEvent.upload(input, file);

    const submitBtn = screen.getByRole('button', { name: /extract statement/i });
    expect(submitBtn).toBeEnabled();

    // Submit form
    await userEvent.click(submitBtn);
    expect(handleSubmit).toHaveBeenCalledWith(file);
  });
});
