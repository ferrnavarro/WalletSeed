import type { StatementHeader as HeaderType } from '../types/api';

interface StatementHeaderProps {
  header: HeaderType;
}

export default function StatementHeader({ header }: StatementHeaderProps) {
  return (
    <div className="glass-card statement-header animate-fade-in">
      <div className="header-meta">
        <span className="card-badge">{header.cardType}</span>
        <span className="page-count">{header.pageCount} pages</span>
      </div>
      <h2>{header.maskedAccount}</h2>
      <div className="statement-period">
        <div className="period-item">
          <span className="period-label">Issue Date</span>
          <span className="period-value">{header.period.issueDate}</span>
        </div>
        <div className="period-divider">→</div>
        <div className="period-item">
          <span className="period-label">Cutoff Date</span>
          <span className="period-value">{header.period.cutoffDate}</span>
        </div>
      </div>
    </div>
  );
}
