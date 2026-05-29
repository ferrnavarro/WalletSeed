interface TotalsPairProps {
  computed: number;
  printed: number | null;
  kind: 'charges' | 'credits' | 'expense' | 'income';
}

export default function TotalsPair({ computed, printed, kind }: TotalsPairProps) {
  const hasMismatch = printed !== null && Math.abs(computed - printed) > 0.005;
  const label = kind.charAt(0).toUpperCase() + kind.slice(1);

  return (
    <div className={`totals-pair ${hasMismatch ? 'totals-mismatch' : ''}`}>
      <span className="totals-label">{label}</span>
      <div className="totals-values">
        <div className="value-group">
          <span className="value-label">Computed:</span>
          <span className="value-amount">${computed.toFixed(2)}</span>
        </div>
        {printed !== null && (
          <div className="value-group">
            <span className="value-label">Printed:</span>
            <span className="value-amount">${printed.toFixed(2)}</span>
          </div>
        )}
      </div>
      {hasMismatch && (
        <span className="mismatch-warning" title="Computed total does not match printed statement subtotal">
          ⚠️ Mismatch
        </span>
      )}
    </div>
  );
}
