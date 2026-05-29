export type ReconciliationStatus = 'ok' | 'needsReview';

export type RowType = 'purchase' | 'financing' | 'payment' | 'adjustment';

export type Direction = 'income' | 'expense';

export interface StatementPeriod {
  issueDate: string; // ISO Date YYYY-MM-DD
  cutoffDate: string;
}

export interface StatementHeader {
  cardType: string;
  maskedAccount: string;
  period: StatementPeriod;
  pageCount: number;
}

export interface Transaction {
  date: string;
  postingDate: string;
  referenceNumber: string;
  sequenceCode: string;
  rowType: RowType;
  description: string;
  amount: number;
  direction: Direction;
  cardLast4: string;
  needsReview: boolean;
  categoryId: string | null;
  categoryName: string | null;
  labelId: string | null;
  labelName: string | null;
  labelUnmapped: boolean;
}

export interface SectionTotals {
  computedCharges: number;
  computedCredits: number;
  printedCharges: number | null;
  printedCredits: number | null;
}

export interface StatementTotals {
  computedExpense: number;
  computedIncome: number;
  printedExpense: number | null;
  printedIncome: number | null;
}

export interface CardholderSection {
  cardLast4: string;
  rawName: string;
  transactions: Transaction[];
  totals: SectionTotals;
  reconciliationStatus: ReconciliationStatus;
}

export interface ExtractedStatementResponse {
  statement: StatementHeader;
  sections: CardholderSection[];
  totals: StatementTotals;
  reconciliationStatus: ReconciliationStatus;
  needsReviewCount: number;
  unmappedCards: string[];
}

export interface ExtractionErrorResponse {
  error: {
    code:
      | 'INVALID_FILE_TYPE'
      | 'EMPTY_FILE'
      | 'FILE_TOO_LARGE'
      | 'PASSWORD_PROTECTED'
      | 'NO_TEXT_EXTRACTABLE'
      | 'UNRECOGNIZED_LAYOUT'
      | 'PARSE_FAILED';
    message: string;
  };
}

export type Result =
  | { ok: true; data: ExtractedStatementResponse }
  | { ok: false; error: ExtractionErrorResponse['error']; httpStatus: number };
