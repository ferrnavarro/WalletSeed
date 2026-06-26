import type { PdfRow, WalletCategory } from '../types/wallet';

interface PerRowPreviewProps {
  row: PdfRow;
  accountName: string;
  categoryName: string | null;
  categories?: WalletCategory[];
}

export default function PerRowPreview({ row, accountName, categoryName, categories }: PerRowPreviewProps) {
  const categoryLabel = categoryName ?? '(no category)';
  const labelSummary = row.previewLabelNames.length > 0 ? row.previewLabelNames.join(', ') : '(no label)';

  return (
    <details className="comparison-card" style={{ marginTop: '0.75rem' }}>
      <summary>Preview payload</summary>
      <div style={{ marginTop: '0.5rem', fontSize: '0.95rem' }}>
        <div>accountId → {accountName}</div>
        <div>recordDate → {row.date}</div>
        <div>amount.value → {row.signedAmount.toFixed(2)}</div>
        <div>amount.currencyCode → {row.currency}</div>
        <div>paymentType → credit_card</div>
        <div>categoryId → {categoryLabel}</div>
        <div>labelIds → {labelSummary}</div>
        <div>note → {row.description}</div>
        <div>counterParty → {row.counterParty ?? '—'}</div>
      </div>
    </details>
  );
}
