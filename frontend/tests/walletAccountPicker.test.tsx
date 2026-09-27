import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import WalletAccountPicker from '../src/components/WalletAccountPicker';

describe('WalletAccountPicker', () => {
  it('renders the accounts and calls the change handler with the selected id', async () => {
    const onChange = vi.fn();
    render(
      <WalletAccountPicker
        accounts={[
          { id: 'acct-1', name: 'Checking', currencyCode: 'USD', accountType: 'checking' },
          { id: 'acct-2', name: 'Savings', currencyCode: 'USD', accountType: 'savings' },
        ]}
        selectedId={null}
        onChange={onChange}
      />
    );

    expect(screen.getByLabelText(/wallet account/i)).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /checking/i })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /savings/i })).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText(/wallet account/i), 'acct-2');

    expect(onChange).toHaveBeenCalledWith('acct-2');
  });
});
