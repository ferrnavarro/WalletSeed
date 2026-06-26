import type { CompareResponse } from '../types/wallet';

interface ComparisonTableProps {
  response: CompareResponse;
}

export default function ComparisonTable({ response }: ComparisonTableProps) {
  return (
    <div className="comparison-grid">
      <div className="comparison-column">
        <h3>PDF rows</h3>
        {response.pdfRows.map((row) => (
          <div key={`pdf-${row.index}`} className="comparison-card">
            <div className="comparison-card__header">
              <strong>Row #{row.index}</strong>
              {row.matchedWalletRecordIds.length > 0 ? <span className="badge">Matches W-{row.matchedWalletRecordIds[0]}</span> : null}
            </div>
            <div>{row.date}</div>
            <div>{row.signedAmount.toFixed(2)}</div>
            <div>{row.description}</div>
            <div>{row.cardholderSectionRawName} • {row.cardLast4}</div>
          </div>
        ))}
      </div>
      <div className="comparison-column">
        <h3>Wallet rows</h3>
        {response.walletRows.map((row) => (
          <div key={`wallet-${row.id}`} className="comparison-card">
            <div className="comparison-card__header">
              <strong>{row.id}</strong>
              {row.claimedByPdfIndices.length > 0 ? <span className="badge">Claimed by row #{row.claimedByPdfIndices[0]}</span> : null}
            </div>
            <div>{row.date}</div>
            <div>{row.signedAmount.toFixed(2)}</div>
            <div>{row.note ?? '—'}</div>
            <div>{row.categoryName ?? '—'}</div>
          </div>
        ))}
      </div>
    </div>
  );
}
