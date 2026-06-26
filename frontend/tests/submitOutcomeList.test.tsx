import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import SubmitOutcomeList from '../src/components/SubmitOutcomeList';

describe('SubmitOutcomeList', () => {
  it('renders success and failure outcomes and forwards retry requests', async () => {
    const onRetryFailedRows = vi.fn();
    render(
      <SubmitOutcomeList
        outcomes={[
          { index: 0, ok: true, walletRecordId: 'w-1', errorMessage: null },
          { index: 1, ok: false, walletRecordId: null, errorMessage: 'bad data' },
        ]}
        onRetryFailedRows={onRetryFailedRows}
      />,
    );

    expect(screen.getByText(/w-1/i)).toBeInTheDocument();
    expect(screen.getByText(/bad data/i)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /retry failed rows/i }));
    expect(onRetryFailedRows).toHaveBeenCalledWith([1]);
  });
});
