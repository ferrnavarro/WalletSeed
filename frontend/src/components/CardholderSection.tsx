import type { CardholderSection as SectionType } from '../types/api';
import TransactionRow from './TransactionRow';
import TotalsPair from './TotalsPair';

interface CardholderSectionProps {
  section: SectionType;
}

export default function CardholderSection({ section }: CardholderSectionProps) {
  return (
    <div className="glass-card cardholder-section animate-fade-in">
      <div className="section-header">
        <h3>Card last 4: {section.cardLast4}</h3>
        <span className="holder-name">{section.rawName}</span>
      </div>

      <div className="table-responsive">
        <table className="transactions-table">
          <thead>
            <tr>
              <th>Date</th>
              <th>Posting Date</th>
              <th>Ref No.</th>
              <th>Seq Code</th>
              <th>Description</th>
              <th className="col-amount-header">Amount</th>
            </tr>
          </thead>
          <tbody>
            {section.transactions.map((tx, index) => (
              <TransactionRow key={index} transaction={tx} />
            ))}
          </tbody>
        </table>
      </div>

      <div className="section-totals-container">
        <TotalsPair 
          computed={section.totals.computedCharges} 
          printed={section.totals.printedCharges} 
          kind="charges" 
        />
        <TotalsPair 
          computed={section.totals.computedCredits} 
          printed={section.totals.printedCredits} 
          kind="credits" 
        />
      </div>
    </div>
  );
}
