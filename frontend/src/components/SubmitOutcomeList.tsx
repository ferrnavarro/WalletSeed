interface SubmitOutcomeListProps {
  outcomes: Array<{ index: number; ok: boolean; walletRecordId?: string | null; errorMessage?: string | null }>;
  onRetryFailedRows?: (failedIndices: number[]) => void;
}

export default function SubmitOutcomeList({ outcomes, onRetryFailedRows }: SubmitOutcomeListProps) {
  const failedIndices = outcomes.filter((outcome) => !outcome.ok).map((outcome) => outcome.index);
  return (
    <div className="comparison-column" style={{ marginTop: '1.5rem' }}>
      <h3>Import outcomes</h3>
      {onRetryFailedRows && failedIndices.length > 0 ? (
        <button type="button" className="btn btn-primary" style={{ marginBottom: '0.75rem' }} onClick={() => onRetryFailedRows(failedIndices)}>
          Retry failed rows
        </button>
      ) : null}
      {outcomes.map((outcome) => (
        <div key={outcome.index} className="comparison-card">
          <div className="comparison-card__header">
            <strong>Row #{outcome.index}</strong>
            <span className="badge">{outcome.ok ? '✅ Imported' : '❌ Failed'}</span>
          </div>
          {outcome.ok ? <div>Wallet ID: {outcome.walletRecordId}</div> : <div>{outcome.errorMessage}</div>}
        </div>
      ))}
    </div>
  );
}
