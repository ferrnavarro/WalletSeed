import type { WalletErrorCode } from '../types/wallet';

interface WalletErrorBannerProps {
  code: WalletErrorCode;
  message: string;
  onRetry?: () => void;
}

const copyByCode: Record<WalletErrorCode, string> = {
  WALLET_NOT_CONFIGURED: 'The Wallet integration is not configured on the server yet. Ask the operator to add the Wallet base URL and JWT before retrying.',
  WALLET_CREDENTIALS_INVALID: 'The Wallet credentials configured for the backend are invalid. Ask the operator to refresh the Wallet JWT and retry.',
  WALLET_UNAVAILABLE: 'The Wallet service is temporarily unavailable. Wait a moment and try again.',
  WALLET_REJECTED: 'The Wallet service rejected the request. Review the submitted payload and try again.',
};

export default function WalletErrorBanner({ code, message, onRetry }: WalletErrorBannerProps) {
  return (
    <div className="error-banner glass-card" role="alert">
      <h3>Wallet import issue</h3>
      <p>{message}</p>
      <p className="form-description">{copyByCode[code]}</p>
      {onRetry ? <button type="button" className="btn btn-primary" onClick={onRetry}>Try again</button> : null}
    </div>
  );
}
