import { useEffect, useState } from 'react';
import UploadForm from '../components/UploadForm';
import WalletAccountPicker from '../components/WalletAccountPicker';
import WalletErrorBanner from '../components/WalletErrorBanner';
import ComparisonTable from '../components/ComparisonTable';
import CategoryDropdown from '../components/CategoryDropdown';
import SubmitOutcomeList from '../components/SubmitOutcomeList';
import PerRowPreview from '../components/PerRowPreview';
import { compare, listAccounts, listCategories, submit } from '../api/walletClient';
import type { CompareResponse, WalletAccount, WalletCategory, WalletErrorCode } from '../types/wallet';

interface WalletImportPageState {
  accounts: WalletAccount[];
  categories: WalletCategory[];
  selectedAccountId: string | null;
  comparison?: CompareResponse;
  selectedRows: Record<number, boolean>;
  categoryByIndex: Record<number, string | null>;
  error?: { code: WalletErrorCode; message: string };
  loading: boolean;
  outcomes?: Array<{ index: number; ok: boolean; walletRecordId?: string | null; errorMessage?: string | null }>;
  file?: File;
  submitting: boolean;
}

export default function WalletImportPage() {
  const [state, setState] = useState<WalletImportPageState>({
    accounts: [],
    categories: [],
    selectedAccountId: null,
    selectedRows: {},
    categoryByIndex: {},
    loading: true,
    submitting: false,
  });

  useEffect(() => {
    void (async () => {
      const accountsResult = await listAccounts();
      const categoriesResult = await listCategories();
      if (accountsResult.ok && categoriesResult.ok) {
        setState((current) => ({ ...current, accounts: accountsResult.data, categories: categoriesResult.data, loading: false }));
      } else {
        setState((current) => ({
          ...current,
          error: accountsResult.ok ? categoriesResult.error : accountsResult.error,
          loading: false,
        }));
      }
    })();
  }, []);

  const handleAccountChange = (accountId: string) => {
    setState((current) => ({ ...current, selectedAccountId: accountId }));
  };

  const handleUpload = async (file: File) => {
    if (!state.selectedAccountId) {
      setState((current) => ({ ...current, error: { code: 'WALLET_REJECTED', message: 'Please select a wallet account first.' } }));
      return;
    }

    setState((current) => ({ ...current, loading: true, error: undefined, outcomes: undefined, file }));
    const result = await compare(file, state.selectedAccountId);
    if (result.ok) {
      const nextSelectedRows = Object.fromEntries(result.data.pdfRows.map((row) => [row.index, row.defaultSelected]));
      const nextCategoryByIndex = Object.fromEntries(result.data.pdfRows.map((row) => [row.index, null]));
      setState((current) => ({
        ...current,
        comparison: result.data,
        selectedRows: nextSelectedRows,
        categoryByIndex: nextCategoryByIndex,
        loading: false,
        submitting: false,
      }));
    } else {
      setState((current) => ({ ...current, error: result.error, loading: false }));
    }
  };

  const toggleSelection = (index: number) => {
    setState((current) => ({
      ...current,
      selectedRows: {
        ...current.selectedRows,
        [index]: !current.selectedRows[index],
      },
    }));
  };

  const handleCategoryChange = (index: number, categoryId: string) => {
    setState((current) => ({
      ...current,
      categoryByIndex: {
        ...current.categoryByIndex,
        [index]: categoryId,
      },
    }));
  };

  const buildSubmitRequest = () => {
    if (!state.comparison || !state.selectedAccountId) {
      return null;
    }

    const selectedRows = state.comparison.pdfRows.filter((row) => state.selectedRows[row.index]);
    return {
      accountId: state.selectedAccountId,
      rows: selectedRows.map((row) => ({
        index: row.index,
        date: row.date,
        signedAmount: row.signedAmount,
        currency: row.currency,
        description: row.description,
        counterParty: row.counterParty,
        cardholderSectionRawName: row.cardholderSectionRawName,
        categoryId: state.categoryByIndex[row.index] ?? '',
      })),
    };
  };

  const handleSubmit = async () => {
    const request = buildSubmitRequest();
    if (!request) {
      return;
    }

    setState((current) => ({ ...current, submitting: true, error: undefined }));
    const result = await submit(request);
    if (result.ok) {
      setState((current) => ({ ...current, outcomes: result.data.outcomes, submitting: false }));
    } else {
      setState((current) => ({ ...current, error: result.error, submitting: false }));
    }
  };

  const handleRetryFailedRows = async (failedIndices: number[]) => {
    const request = buildSubmitRequest();
    if (!request) {
      return;
    }

    const rows = request.rows.filter((row) => failedIndices.includes(row.index));
    if (rows.length === 0) {
      return;
    }

    setState((current) => ({ ...current, submitting: true, error: undefined }));
    const result = await submit({ ...request, rows });
    if (result.ok) {
      setState((current) => ({ ...current, outcomes: result.data.outcomes, submitting: false }));
    } else {
      setState((current) => ({ ...current, error: result.error, submitting: false }));
    }
  };

  const handleReloadComparison = async () => {
    if (!state.file || !state.selectedAccountId) {
      return;
    }

    setState((current) => ({ ...current, loading: true, error: undefined, outcomes: undefined }));
    const result = await compare(state.file, state.selectedAccountId);
    if (result.ok) {
      const nextSelectedRows = Object.fromEntries(result.data.pdfRows.map((row) => [row.index, row.defaultSelected]));
      const nextCategoryByIndex = Object.fromEntries(result.data.pdfRows.map((row) => [row.index, null]));
      setState((current) => ({
        ...current,
        comparison: result.data,
        selectedRows: nextSelectedRows,
        categoryByIndex: nextCategoryByIndex,
        loading: false,
        submitting: false,
      }));
    } else {
      setState((current) => ({ ...current, error: result.error, loading: false }));
    }
  };

  const canSubmit = Boolean(state.comparison) && state.comparison!.pdfRows.some((row) => state.selectedRows[row.index]) && state.comparison!.pdfRows.filter((row) => state.selectedRows[row.index]).every((row) => (state.categoryByIndex[row.index] ?? '').length > 0);

  return (
    <section className="glass-card wallet-page">
      <h2>Wallet Import</h2>
      <p className="form-description">
        Pick an account and upload a BAC statement to compare it with your Wallet records.
      </p>

      {state.error ? <WalletErrorBanner code={state.error.code} message={state.error.message} onRetry={() => setState((current) => ({ ...current, error: undefined }))} /> : null}

      <WalletAccountPicker
        accounts={state.accounts}
        selectedId={state.selectedAccountId}
        onChange={handleAccountChange}
      />

      {state.selectedAccountId ? (
        <UploadForm onSubmit={handleUpload} onLocalError={(payload) => setState((current) => ({ ...current, error: { code: 'WALLET_REJECTED', message: payload.message } }))} />
      ) : null}

      {state.loading ? <p className="form-description">Loading accounts…</p> : null}

      {state.comparison ? (
        <>
          <ComparisonTable response={state.comparison} />
          <div className="comparison-card" style={{ marginTop: '1rem' }}>
            <h3>Select rows to import</h3>
            <div className="form-description">Selected: {state.comparison.pdfRows.filter((row) => state.selectedRows[row.index]).length}</div>
            {state.comparison.pdfRows.map((row) => (
              <div key={`row-select-${row.index}`} className="comparison-card" style={{ marginBottom: '0.75rem' }}>
                <div className="comparison-card__header">
                  <strong>Row #{row.index} • {row.description}</strong>
                  <label>
                    <input type="checkbox" checked={Boolean(state.selectedRows[row.index])} onChange={() => toggleSelection(row.index)} />
                    <span style={{ marginLeft: '0.5rem' }}>Select</span>
                  </label>
                </div>
                <div className="form-description">Import this transaction to Wallet</div>
                <div>{row.date} • {row.signedAmount.toFixed(2)}</div>
                {state.selectedRows[row.index] ? (
                  <>
                    <CategoryDropdown
                      categories={state.comparison.categories.length > 0 ? state.comparison.categories : state.categories}
                      value={state.categoryByIndex[row.index] ?? null}
                      onChange={(categoryId) => handleCategoryChange(row.index, categoryId)}
                      label={`Category for row ${row.index}`}
                    />
                    {((state.categoryByIndex[row.index] ?? '').length === 0) ? <div className="form-description">Needs category</div> : null}
                    <PerRowPreview row={row} accountName={state.comparison.account.name} categoryName={state.categories.find((category) => category.id === state.categoryByIndex[row.index])?.name ?? null} />
                  </>
                ) : null}
              </div>
            ))}
          </div>
          <div className="comparison-card" style={{ marginTop: '1rem' }}>
            <button className="btn btn-primary" disabled={!canSubmit || state.submitting} onClick={handleSubmit}>
              {state.submitting ? 'Submitting…' : 'Import to Wallet'}
            </button>
            {state.outcomes ? (
              <button type="button" className="btn btn-secondary" style={{ marginLeft: '0.75rem' }} onClick={handleReloadComparison}>
                Reload comparison from Wallet
              </button>
            ) : null}
          </div>
          {state.outcomes ? <SubmitOutcomeList outcomes={state.outcomes} onRetryFailedRows={handleRetryFailedRows} /> : null}
        </>
      ) : null}
    </section>
  );
}
