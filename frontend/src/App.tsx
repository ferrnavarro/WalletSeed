import { useReducer } from 'react';
import type { ExtractedStatementResponse, ExtractionErrorResponse } from './types/api';
import { extractStatement } from './api/statementsClient';
import UploadForm from './components/UploadForm';
import StatementHeader from './components/StatementHeader';
import CardholderSection from './components/CardholderSection';
import TotalsPair from './components/TotalsPair';
import ErrorBanner from './components/ErrorBanner';

type State =
  | { kind: 'idle' }
  | { kind: 'uploading' }
  | { kind: 'success'; data: ExtractedStatementResponse }
  | { kind: 'error'; error: ExtractionErrorResponse['error']; httpStatus: number };

type Action =
  | { type: 'START_UPLOAD' }
  | { type: 'UPLOAD_SUCCESS'; payload: ExtractedStatementResponse }
  | { type: 'UPLOAD_ERROR'; payload: { error: ExtractionErrorResponse['error']; status: number } }
  | { type: 'RESET' };

function reducer(state: State, action: Action): State {
  switch (action.type) {
    case 'START_UPLOAD':
      return { kind: 'uploading' };
    case 'UPLOAD_SUCCESS':
      return { kind: 'success', data: action.payload };
    case 'UPLOAD_ERROR':
      return {
        kind: 'error',
        error: action.payload.error,
        httpStatus: action.payload.status,
      };
    case 'RESET':
      return { kind: 'idle' };
    default:
      return state;
  }
}

export default function App() {
  const [state, dispatch] = useReducer(reducer, { kind: 'idle' });

  const handleUpload = async (file: File) => {
    dispatch({ type: 'START_UPLOAD' });
    const result = await extractStatement(file);
    if (result.ok) {
      dispatch({ type: 'UPLOAD_SUCCESS', payload: result.data });
    } else {
      dispatch({ type: 'UPLOAD_ERROR', payload: { error: result.error, status: result.httpStatus } });
    }
  };

  const handleLocalError = (payload: { code: any; message: string }) => {
    dispatch({ type: 'UPLOAD_ERROR', payload: { error: payload, status: 400 } });
  };

  return (
    <div className="app-container">
      <header className="app-header">
        <h1>WalletSeed — Statement Extract</h1>
      </header>

      <main className="app-content">
        {(state.kind === 'idle' || state.kind === 'error') && (
          <>
            {state.kind === 'error' && (
              <ErrorBanner error={state.error} httpStatus={state.httpStatus} />
            )}
            <UploadForm onSubmit={handleUpload} onLocalError={handleLocalError} />
          </>
        )}

        {state.kind === 'uploading' && (
          <div className="loading-state glass-card">
            <p>Processing statement PDF, please wait...</p>
          </div>
        )}

        {state.kind === 'success' && (
          <div className="result-state animate-fade-in">
            <button onClick={() => dispatch({ type: 'RESET' })} className="btn btn-back">
              Upload Another Statement
            </button>
            
            <StatementHeader header={state.data.statement} />
            
            <div className="sections-container" style={{ marginTop: '2rem', display: 'flex', flexDirection: 'column', gap: '2rem' }}>
              {state.data.sections.map((section, index) => (
                <CardholderSection key={index} section={section} />
              ))}
            </div>
            
            {/* Statement Totals Footer */}
            <div className="glass-card statement-footer animate-fade-in" style={{ marginTop: '2.5rem' }}>
              <div className="footer-header" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.5rem', paddingBottom: '0.75rem', borderBottom: '1px solid var(--border)' }}>
                <h3>Statement Summary</h3>
                <span className={`reconciliation-badge status--${state.data.reconciliationStatus}`}>
                  Reconciliation: {state.data.reconciliationStatus.toUpperCase()}
                </span>
              </div>
              <div className="statement-totals-grid" style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '1.5rem' }}>
                <TotalsPair 
                  computed={state.data.totals.computedExpense} 
                  printed={state.data.totals.printedExpense} 
                  kind="expense" 
                />
                <TotalsPair 
                  computed={state.data.totals.computedIncome} 
                  printed={state.data.totals.printedIncome} 
                  kind="income" 
                />
              </div>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}
