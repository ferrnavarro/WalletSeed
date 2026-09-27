import type { WalletAccount } from '../types/wallet';

interface WalletAccountPickerProps {
  accounts: WalletAccount[];
  selectedId: string | null;
  onChange: (accountId: string) => void;
}

export default function WalletAccountPicker({ accounts, selectedId, onChange }: WalletAccountPickerProps) {
  return (
    <label className="wallet-picker">
      <span>Wallet account</span>
      <select
        aria-label="Wallet account"
        value={selectedId ?? ''}
        onChange={(event) => onChange(event.target.value)}
      >
        <option value="">Select an account</option>
        {accounts.map((account) => (
          <option key={account.id} value={account.id}>
            {account.name} ({account.currencyCode})
          </option>
        ))}
      </select>
    </label>
  );
}
