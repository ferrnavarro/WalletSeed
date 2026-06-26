import { useEffect, useState } from 'react';
import UploadForm from '../components/UploadForm';
import WalletAccountPicker from '../components/WalletAccountPicker';
import WalletErrorBanner from '../components/WalletErrorBanner';
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

  const [collapsedSections, setCollapsedSections] = useState<Record<string, boolean>>({});

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

  // Group PDF rows by cardholder section
  interface GroupedPdfSection {
    cardLast4: string;
    rawName: string;
    rows: Array<(typeof state.comparison.pdfRows)[number]>;
  }

  const groupedSections: GroupedPdfSection[] = [];
  if (state.comparison) {
    for (const row of state.comparison.pdfRows) {
      let section = groupedSections.find(
        (s) => s.cardLast4 === row.cardLast4 && s.rawName === row.cardholderSectionRawName
      );
      if (!section) {
        section = {
          cardLast4: row.cardLast4,
          rawName: row.cardholderSectionRawName,
          rows: [],
        };
        groupedSections.push(section);
      }
      section.rows.push(row);
    }
  }

  const toggleSectionCollapse = (sectionKey: string) => {
    setCollapsedSections((prev) => ({
      ...prev,
      [sectionKey]: !prev[sectionKey],
    }));
  };

  const toggleSelectAllForSection = (section: GroupedPdfSection, select: boolean) => {
    setState((current) => {
      const nextSelectedRows = { ...current.selectedRows };
      for (const row of section.rows) {
        nextSelectedRows[row.index] = select;
      }
      return {
        ...current,
        selectedRows: nextSelectedRows,
      };
    });
  };

  const getCommonCategoryForSection = (section: GroupedPdfSection): string | null => {
    if (section.rows.length === 0) return null;
    const firstCat = state.categoryByIndex[section.rows[0].index] ?? '';
    const allSame = section.rows.every((row) => (state.categoryByIndex[row.index] ?? '') === firstCat);
    return allSame ? firstCat : '';
  };

  const handleApplyCategoryToAllForSection = (section: GroupedPdfSection, categoryId: string) => {
    setState((current) => {
      const nextCategoryByIndex = { ...current.categoryByIndex };
      for (const row of section.rows) {
        nextCategoryByIndex[row.index] = categoryId;
      }
      return {
        ...current,
        categoryByIndex: nextCategoryByIndex,
      };
    });
  };

  const allCategories = state.comparison?.categories && state.comparison.categories.length > 0 
    ? state.comparison.categories 
    : state.categories;

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
          <div style={{ marginTop: '2rem' }}>
            <h3 style={{ fontFamily: 'var(--font-family-heading)', fontSize: '1.5rem', marginBottom: '0.5rem' }}>Select rows to import</h3>
            <div className="form-description" style={{ marginBottom: '1.5rem' }}>
              Selected: {state.comparison.pdfRows.filter((row) => state.selectedRows[row.index]).length}
            </div>

            {groupedSections.map((section) => {
              const sectionKey = `${section.rawName}-${section.cardLast4}`;
              const isCollapsed = Boolean(collapsedSections[sectionKey]);
              const allSelected = section.rows.every((row) => state.selectedRows[row.index]);
              const someSelected = section.rows.some((row) => state.selectedRows[row.index]);

              return (
                <div key={sectionKey} className="glass-card cardholder-section animate-fade-in" style={{ marginTop: '1.5rem', padding: '1.5rem 2rem' }}>
                  <div 
                    className="section-header" 
                    style={{ 
                      display: 'flex', 
                      justifyContent: 'space-between', 
                      alignItems: 'center', 
                      marginBottom: isCollapsed ? '0' : '1rem',
                      cursor: 'pointer',
                      userSelect: 'none'
                    }}
                    onClick={() => toggleSectionCollapse(sectionKey)}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                      <span style={{ 
                        fontSize: '1rem', 
                        color: 'var(--color-text-dim)', 
                        transition: 'transform var(--transition-fast)', 
                        transform: isCollapsed ? 'rotate(-90deg)' : 'rotate(0deg)',
                        display: 'inline-block'
                      }}>
                        ▼
                      </span>
                      <div>
                        <h3 style={{ margin: 0, fontSize: '1.15rem' }}>Card last 4: {section.cardLast4}</h3>
                        <span className="holder-name" style={{ fontSize: '0.9rem', color: 'var(--color-text-muted)', display: 'block', marginTop: '0.1rem' }}>{section.rawName}</span>
                      </div>
                    </div>

                    <div style={{ display: 'flex', alignItems: 'center', gap: '1.5rem' }} onClick={(e) => e.stopPropagation()}>
                      <div 
                        onClick={() => {
                          toggleSelectAllForSection(section, !allSelected);
                        }}
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
                          value={getCommonCategoryForSection(section)}
                          onChange={(categoryId) => handleApplyCategoryToAllForSection(section, categoryId)}
                          label={`Category for all in card ${section.cardLast4}`}
                          inline={true}
                        />
                      </div>
                    </div>
                  </div>

                  {!isCollapsed && (
                    <div className="table-responsive">
                      <table className="transactions-table">
                        <thead>
                          <tr>
                            <th style={{ width: '100px' }}>Select</th>
                            <th style={{ width: '120px' }}>Date</th>
                            <th>Description</th>
                            <th className="col-amount-header" style={{ width: '120px' }}>Amount</th>
                            <th style={{ width: '180px' }}>Match Status</th>
                            <th style={{ width: '220px' }}>Category</th>
                          </tr>
                        </thead>
                        <tbody>
                          {section.rows.map((row) => {
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
                                  {row.counterParty && (
                                    <div style={{ fontSize: '0.8rem', color: 'var(--color-text-dim)', marginTop: '0.25rem' }}>
                                      Merchant: {row.counterParty}
                                    </div>
                                  )}
                                </td>
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
                                          accountName={state.comparison.account.name}
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
                  )}
                </div>
              );
            })}
          </div>

          {/* Collapsible section for existing Wallet records */}
          <details className="glass-card cardholder-section animate-fade-in" style={{ marginTop: '2rem', padding: '1.5rem 2rem' }}>
            <summary style={{ fontFamily: 'var(--font-family-heading)', fontSize: '1.25rem', fontWeight: 600, cursor: 'pointer', outline: 'none' }}>
              Existing Wallet Records in this Period ({state.comparison.walletRows.length})
            </summary>
            <div style={{ marginTop: '1.5rem' }}>
              {state.comparison.walletRows.length === 0 ? (
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
                      {state.comparison.walletRows.map((wRow) => {
                        const isIncome = wRow.signedAmount > 0;
                        return (
                          <tr key={wRow.id} className="transaction-row">
                            <td className="col-ref">W-{wRow.id}</td>
                            <td className="col-date">{wRow.date}</td>
                            <td className="col-desc">
                              <span style={{ fontWeight: 600 }}>{wRow.note ?? '—'}</span>
                              {wRow.counterParty && (
                                <div style={{ fontSize: '0.8rem', color: 'var(--color-text-dim)', marginTop: '0.25rem' }}>
                                  Merchant: {wRow.counterParty}
                                </div>
                              )}
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
