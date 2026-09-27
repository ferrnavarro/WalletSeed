import { useEffect, useState } from 'react';
import FileUploadForm from '../components/FileUploadForm';
import WalletAccountPicker from '../components/WalletAccountPicker';
import WalletErrorBanner from '../components/WalletErrorBanner';
import CategoryDropdown from '../components/CategoryDropdown';
import SubmitOutcomeList from '../components/SubmitOutcomeList';
import PerRowPreview from '../components/PerRowPreview';
import { compareCsv, compareExcel, listAccounts, listCategories, submit } from '../api/walletClient';
import type { FileImportCompareResponse, WalletAccount, WalletCategory, WalletErrorCode, WalletRow, PdfRow, FileImportError } from '../types/wallet';

interface FileImportPageState {
  accounts: WalletAccount[];
  categories: WalletCategory[];
  selectedAccountId: string | null;
  comparison?: FileImportCompareResponse;
  selectedRows: Record<number, boolean>;
  categoryByIndex: Record<number, string | null>;
  error?: { code: WalletErrorCode; message: string };
  loading: boolean;
  outcomes?: Array<{ index: number; ok: boolean; walletRecordId?: string | null; errorMessage?: string | null }>;
  files?: File[];
  submitting: boolean;
}

function isExcelFile(file: File): boolean {
  return file.name.toLowerCase().endsWith('.xlsx');
}

/** Merge multiple compare responses into one, re-basing row indices so they stay unique. */
function mergeComparisons(responses: FileImportCompareResponse[]): FileImportCompareResponse {
  const first = responses[0];
  const pdfRows: PdfRow[] = [];
  const walletRowsById = new Map<string, WalletRow>();
  const fileErrors: FileImportError[] = [];
  let minFrom = first.window.from;
  let maxTo = first.window.to;

  for (const response of responses) {
    const indexOffset = pdfRows.length;
    for (const row of response.pdfRows) {
      pdfRows.push({ ...row, index: indexOffset + row.index });
    }
    for (const walletRow of response.walletRows) {
      if (!walletRowsById.has(walletRow.id)) {
        walletRowsById.set(walletRow.id, walletRow);
      }
    }
    fileErrors.push(...response.fileErrors);
    if (response.window.from < minFrom) minFrom = response.window.from;
    if (response.window.to > maxTo) maxTo = response.window.to;
  }

  return {
    window: { from: minFrom, to: maxTo, issueDate: first.window.issueDate, cutoffDate: first.window.cutoffDate },
    account: first.account,
    categories: first.categories.length > 0 ? first.categories : (responses.find((r) => r.categories.length > 0)?.categories ?? []),
    pdfRows,
    walletRows: Array.from(walletRowsById.values()),
    unmappedSections: [],
    fileErrors,
  };
}

export default function FileImportPage() {
  const [state, setState] = useState<FileImportPageState>({
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
      } else if (!accountsResult.ok) {
        setState((current) => ({ ...current, error: accountsResult.error, loading: false }));
      } else if (!categoriesResult.ok) {
        setState((current) => ({ ...current, error: categoriesResult.error, loading: false }));
      }
    })();
  }, []);

  const handleAccountChange = (accountId: string) => {
    setState((current) => ({ ...current, selectedAccountId: accountId }));
  };

  const applyComparison = (data: FileImportCompareResponse) => {
    const nextSelectedRows = Object.fromEntries(data.pdfRows.map((row) => [row.index, row.defaultSelected]));
    const nextCategoryByIndex = Object.fromEntries(data.pdfRows.map((row) => [row.index, null]));
    setState((current) => ({
      ...current,
      comparison: data,
      selectedRows: nextSelectedRows,
      categoryByIndex: nextCategoryByIndex,
      loading: false,
      submitting: false,
    }));
  };

  const runCompare = async (files: File[], accountId: string) => {
    const csvFiles = files.filter((f) => !isExcelFile(f));
    const excelFiles = files.filter(isExcelFile);

    const requests: Promise<Awaited<ReturnType<typeof compareCsv>>>[] = [];
    if (csvFiles.length > 0) requests.push(compareCsv(csvFiles, accountId));
    if (excelFiles.length > 0) requests.push(compareExcel(excelFiles, accountId));

    const results = await Promise.all(requests);
    const failures = results.filter((r) => !r.ok);
    if (failures.length > 0 && failures.length === results.length) {
      return { ok: false as const, error: failures[0].ok ? undefined : failures[0].error };
    }

    const successes = results.filter((r) => r.ok).map((r) => (r.ok ? r.data : undefined)).filter((d): d is FileImportCompareResponse => Boolean(d));
    const partialErrors: FileImportError[] = failures.map((f) => (f.ok ? undefined : { fileName: '', message: f.error.message })).filter((e): e is FileImportError => Boolean(e));
    const merged = mergeComparisons(successes);
    return { ok: true as const, data: { ...merged, fileErrors: [...merged.fileErrors, ...partialErrors] } };
  };

  const handleUpload = async (files: File[]) => {
    if (!state.selectedAccountId) {
      setState((current) => ({ ...current, error: { code: 'WALLET_REJECTED', message: 'Please select a wallet account first.' } }));
      return;
    }

    setState((current) => ({ ...current, loading: true, error: undefined, outcomes: undefined, files }));
    const result = await runCompare(files, state.selectedAccountId);
    if (result.ok) {
      applyComparison(result.data);
    } else if (result.error) {
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

  const toggleSelectAll = (select: boolean) => {
    setState((current) => {
      if (!current.comparison) {
        return current;
      }
      const nextSelectedRows = { ...current.selectedRows };
      for (const row of current.comparison.pdfRows) {
        nextSelectedRows[row.index] = select;
      }
      return { ...current, selectedRows: nextSelectedRows };
    });
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

  const handleApplyCategoryToAll = (categoryId: string) => {
    setState((current) => {
      if (!current.comparison) {
        return current;
      }
      const nextCategoryByIndex = { ...current.categoryByIndex };
      for (const row of current.comparison.pdfRows) {
        nextCategoryByIndex[row.index] = categoryId;
      }
      return { ...current, categoryByIndex: nextCategoryByIndex };
    });
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
    if (!state.files || !state.selectedAccountId) {
      return;
    }

    setState((current) => ({ ...current, loading: true, error: undefined, outcomes: undefined }));
    const result = await runCompare(state.files, state.selectedAccountId);
    if (result.ok) {
      applyComparison(result.data);
    } else if (result.error) {
      setState((current) => ({ ...current, error: result.error, loading: false }));
    }
  };

  const selectedRowList = state.comparison?.pdfRows.filter((row) => state.selectedRows[row.index]) ?? [];
  const canSubmit = selectedRowList.length > 0 && selectedRowList.every((row) => (state.categoryByIndex[row.index] ?? '').length > 0);

  const comparison = state.comparison;

  const allCategories = comparison?.categories && comparison.categories.length > 0
    ? comparison.categories
    : state.categories;

  // Display ordering: newest date first. Indices preserved (used for selection/category/submit mapping).
  const sortedPdfRows = comparison ? [...comparison.pdfRows].sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : a.index - b.index)) : [];
  const sortedWalletRows = comparison ? [...comparison.walletRows].sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : (a.id < b.id ? -1 : a.id > b.id ? 1 : 0))) : [];

  const allSelected = Boolean(comparison) && comparison!.pdfRows.length > 0 && comparison!.pdfRows.every((row) => state.selectedRows[row.index]);
  const someSelected = selectedRowList.length > 0;

  const commonCategory = (() => {
    if (!comparison || comparison.pdfRows.length === 0) return null;
    const firstCat = state.categoryByIndex[comparison.pdfRows[0].index] ?? '';
    const allSame = comparison.pdfRows.every((row) => (state.categoryByIndex[row.index] ?? '') === firstCat);
    return allSame ? firstCat : '';
  })();

  return (
    <section className="glass-card wallet-page">
      <h2>File Import</h2>
      <p className="form-description">
        Pick an account and upload bank statement files (BAC CSV or Promerica Excel) to compare them with your Wallet records.
      </p>

      {state.error ? <WalletErrorBanner code={state.error.code} message={state.error.message} onRetry={() => setState((current) => ({ ...current, error: undefined }))} /> : null}

      <WalletAccountPicker
        accounts={state.accounts}
        selectedId={state.selectedAccountId}
        onChange={handleAccountChange}
      />

      {state.selectedAccountId ? (
        <FileUploadForm onSubmit={handleUpload} onLocalError={(payload) => setState((current) => ({ ...current, error: { code: 'WALLET_REJECTED', message: payload.message } }))} />
      ) : null}

      {state.loading ? <p className="form-description">Loading…</p> : null}

      {comparison ? (
        <>
          {comparison.fileErrors.length > 0 ? (
            <div className="error-banner glass-card" role="alert" style={{ marginTop: '1rem' }}>
              <h3>Some files could not be parsed</h3>
              {comparison.fileErrors.map((fileError) => (
                <p key={fileError.fileName}>
                  <strong>{fileError.fileName}</strong>: {fileError.message}
                </p>
              ))}
            </div>
          ) : null}

          <div style={{ marginTop: '2rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: '1rem', marginBottom: '1.5rem' }}>
              <div>
                <h3 style={{ fontFamily: 'var(--font-family-heading)', fontSize: '1.5rem', marginBottom: '0.5rem' }}>Select rows to import</h3>
                <div className="form-description">
                  Selected: {selectedRowList.length} of {comparison.pdfRows.length}
                </div>
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: '1.5rem' }}>
                <div
                  onClick={() => toggleSelectAll(!allSelected)}
                  style={{ display: 'inline-flex', alignItems: 'center', cursor: 'pointer', fontSize: '0.9rem' }}
                >
                  <input
                    type="checkbox"
                    checked={allSelected}
                    aria-label="Toggle all rows"
                    readOnly
                    ref={(el) => {
                      if (el) {
                        el.indeterminate = someSelected && !allSelected;
                      }
                    }}
                  />
                  <span style={{ marginLeft: '0.5rem', fontWeight: 500 }}>Select All</span>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                  <span style={{ fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>Apply Category:</span>
                  <CategoryDropdown
                    categories={allCategories}
                    value={commonCategory}
                    onChange={handleApplyCategoryToAll}
                    label="Category for all rows"
                    inline={true}
                  />
                </div>
              </div>
            </div>

            <div className="table-responsive">
              <table className="transactions-table">
                <thead>
                  <tr>
                    <th style={{ width: '100px' }}>Select</th>
                    <th style={{ width: '120px' }}>Date</th>
                    <th>Description</th>
                    <th style={{ width: '100px' }}>Card</th>
                    <th className="col-amount-header" style={{ width: '120px' }}>Amount</th>
                    <th style={{ width: '180px' }}>Match Status</th>
                    <th style={{ width: '220px' }}>Category</th>
                  </tr>
                </thead>
                <tbody>
                  {sortedPdfRows.map((row) => {
                    const isSelected = Boolean(state.selectedRows[row.index]);
                    const isIncome = row.signedAmount > 0;
                    const selectedCategoryName = allCategories.find((c) => c.id === state.categoryByIndex[row.index])?.name ?? null;

                    return (
                      <tr key={row.index} className={`transaction-row ${row.currencyMismatch ? 'needs-review' : ''}`}>
                        <td>
                          <label style={{ display: 'inline-flex', alignItems: 'center', cursor: 'pointer' }}>
                            <input
                              type="checkbox"
                              checked={isSelected}
                              onChange={() => toggleSelection(row.index)}
                            />
                            <span style={{ marginLeft: '0.5rem' }}>Select</span>
                          </label>
                        </td>
                        <td className="col-date">{row.date}</td>
                        <td className="col-desc">
                          <span style={{ fontWeight: 600 }}>{row.description}</span>
                        </td>
                        <td className="col-ref">{row.cardLast4}</td>
                        <td className="col-amount">
                          <span className={`direction-badge direction--${isIncome ? 'income' : 'expense'}`}>
                            {isIncome ? '+' : '-'}${Math.abs(row.signedAmount).toFixed(2)}
                          </span>
                        </td>
                        <td>
                          {row.matchedWalletRecordIds.length > 0 ? (
                            <span className="badge">
                              Matches W-{row.matchedWalletRecordIds.join(', ')}
                            </span>
                          ) : (
                            <span style={{ color: 'var(--color-text-dim)', fontSize: '0.85rem' }}>No match</span>
                          )}
                          {row.currencyMismatch && (
                            <div style={{ color: 'var(--mismatch)', fontSize: '0.8rem', marginTop: '0.25rem' }}>
                              ⚠️ Currency Mismatch ({row.currency})
                            </div>
                          )}
                        </td>
                        <td>
                          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                            {isSelected ? (
                              <>
                                <CategoryDropdown
                                  categories={allCategories}
                                  value={state.categoryByIndex[row.index] ?? null}
                                  onChange={(categoryId) => handleCategoryChange(row.index, categoryId)}
                                  label={`Category for row ${row.index}`}
                                  inline={true}
                                />
                                {((state.categoryByIndex[row.index] ?? '').length === 0) ? (
                                  <div className="form-description" style={{ color: 'var(--mismatch)', margin: 0, fontSize: '0.8rem' }}>
                                    Needs category
                                  </div>
                                ) : null}
                                <PerRowPreview
                                  row={row}
                                  accountName={comparison.account.name}
                                  categoryName={selectedCategoryName}
                                />
                              </>
                            ) : (
                              <span style={{ color: 'var(--color-text-dim)', fontSize: '0.85rem' }}>Not imported</span>
                            )}
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>

          <details className="glass-card cardholder-section animate-fade-in" style={{ marginTop: '2rem', padding: '1.5rem 2rem' }}>
            <summary style={{ fontFamily: 'var(--font-family-heading)', fontSize: '1.25rem', fontWeight: 600, cursor: 'pointer', outline: 'none' }}>
              Existing Wallet Records in this Period ({comparison.walletRows.length})
            </summary>
            <div style={{ marginTop: '1.5rem' }}>
              {comparison.walletRows.length === 0 ? (
                <p className="form-description">No existing records in this period.</p>
              ) : (
                <div className="table-responsive">
                  <table className="transactions-table">
                    <thead>
                      <tr>
                        <th style={{ width: '120px' }}>ID</th>
                        <th style={{ width: '120px' }}>Date</th>
                        <th>Description / Note</th>
                        <th className="col-amount-header" style={{ width: '120px' }}>Amount</th>
                        <th style={{ width: '180px' }}>Category</th>
                        <th style={{ width: '180px' }}>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {sortedWalletRows.map((wRow) => {
                        const isIncome = wRow.signedAmount > 0;
                        return (
                          <tr key={wRow.id} className="transaction-row">
                            <td className="col-ref">W-{wRow.id}</td>
                            <td className="col-date">{wRow.date}</td>
                            <td className="col-desc">
                              <span style={{ fontWeight: 600 }}>{wRow.note ?? '—'}</span>
                            </td>
                            <td className="col-amount">
                              <span className={`direction-badge direction--${isIncome ? 'income' : 'expense'}`}>
                                {isIncome ? '+' : '-'}${Math.abs(wRow.signedAmount).toFixed(2)}
                              </span>
                            </td>
                            <td>{wRow.categoryName ?? '—'}</td>
                            <td>
                              {wRow.claimedByPdfIndices.length > 0 ? (
                                <span className="badge">
                                  Claimed by row #{wRow.claimedByPdfIndices.join(', ')}
                                </span>
                              ) : (
                                <span style={{ color: 'var(--color-text-dim)', fontSize: '0.85rem' }}>Unclaimed</span>
                              )}
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          </details>

          <div className="comparison-card" style={{ marginTop: '2rem' }}>
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
