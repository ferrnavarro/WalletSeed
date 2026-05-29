import type { Transaction } from '../types/api';

interface TransactionRowProps {
  transaction: Transaction;
}

export default function TransactionRow({ transaction }: TransactionRowProps) {
  const isIncome = transaction.direction === 'income';
  
  return (
    <tr className={`transaction-row ${transaction.needsReview ? 'needs-review' : ''}`}>
      <td className="col-date">{transaction.date}</td>
      <td className="col-date">{transaction.postingDate}</td>
      <td className="col-ref">{transaction.referenceNumber}</td>
      <td className="col-seq">{transaction.sequenceCode}</td>
      <td className="col-desc">{transaction.description}</td>
      <td className="col-amount">
        <span className={`direction-badge direction--${transaction.direction}`}>
          {isIncome ? '+' : '-'}${transaction.amount.toFixed(2)}
        </span>
      </td>
    </tr>
  );
}
