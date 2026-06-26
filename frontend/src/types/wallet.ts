export type WalletErrorCode =
  | 'WALLET_NOT_CONFIGURED'
  | 'WALLET_CREDENTIALS_INVALID'
  | 'WALLET_UNAVAILABLE'
  | 'WALLET_REJECTED';

export interface WalletAccount {
  id: string;
  name: string;
  currencyCode: string;
  accountType: string;
}

export interface WalletCategory {
  id: string;
  name: string;
  color?: string | null;
}

export interface StatementWindow {
  from: string;
  to: string;
  issueDate: string;
  cutoffDate: string;
}

export interface PdfRow {
  index: number;
  date: string;
  signedAmount: number;
  currency: string;
  description: string;
  counterParty?: string | null;
  cardholderSectionRawName: string;
  cardLast4: string;
  matchedWalletRecordIds: string[];
  defaultSelected: boolean;
  currencyMismatch: boolean;
  previewLabelIds: string[];
  previewLabelNames: string[];
}

export interface WalletRow {
  id: string;
  date: string;
  signedAmount: number;
  currency: string;
  note?: string | null;
  counterParty?: string | null;
  categoryName?: string | null;
  claimedByPdfIndices: number[];
}

export interface CompareResponse {
  window: StatementWindow;
  account: WalletAccount;
  categories: WalletCategory[];
  pdfRows: PdfRow[];
  walletRows: WalletRow[];
  unmappedSections: string[];
}

export interface SubmitRequestRow {
  index: number;
  date: string;
  signedAmount: number;
  currency: string;
  description: string;
  counterParty?: string | null;
  cardholderSectionRawName: string;
  categoryId: string;
}

export interface SubmitRequest {
  accountId: string;
  rows: SubmitRequestRow[];
}

export interface SubmitOutcome {
  index: number;
  ok: boolean;
  walletRecordId?: string | null;
  errorMessage?: string | null;
}

export interface SubmitResponse {
  outcomes: SubmitOutcome[];
}

export interface WalletErrorResponse {
  error: {
    code: WalletErrorCode;
    message: string;
  };
}
