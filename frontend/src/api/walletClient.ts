import type { WalletErrorResponse, WalletAccount, WalletCategory, CompareResponse, CsvCompareResponse, SubmitRequest, SubmitResponse } from '../types/wallet';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5080';

export type WalletResult<T> =
  | { ok: true; data: T }
  | { ok: false; error: WalletErrorResponse['error']; httpStatus: number };

async function readJson<T>(response: Response): Promise<T> {
  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

function toErrorResult<T>(status: number, payload: unknown): WalletResult<T> {
  const error = (payload as WalletErrorResponse | undefined)?.error ?? {
    code: 'WALLET_UNAVAILABLE',
    message: 'Wallet import is not available right now.',
  };

  return { ok: false, error, httpStatus: status };
}

export async function listAccounts(): Promise<WalletResult<WalletAccount[]>> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/wallet/accounts`, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });

    if (!response.ok) {
      return toErrorResult<WalletAccount[]>(response.status, await readJson<unknown>(response));
    }

    const payload = await readJson<{ accounts?: WalletAccount[] }>(response);
    return { ok: true, data: payload?.accounts ?? [] };
  } catch {
    return { ok: false, error: { code: 'WALLET_UNAVAILABLE', message: 'Unable to reach the wallet API.' }, httpStatus: 503 };
  }
}

export async function listCategories(): Promise<WalletResult<WalletCategory[]>> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/wallet/categories`, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });

    if (!response.ok) {
      return toErrorResult<WalletCategory[]>(response.status, await readJson<unknown>(response));
    }

    const payload = await readJson<{ categories?: WalletCategory[] }>(response);
    return { ok: true, data: payload?.categories ?? [] };
  } catch {
    return { ok: false, error: { code: 'WALLET_UNAVAILABLE', message: 'Unable to reach the wallet API.' }, httpStatus: 503 };
  }
}

export async function compare(file: File, accountId: string): Promise<WalletResult<CompareResponse>> {
  try {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('accountId', accountId);

    const response = await fetch(`${API_BASE_URL}/api/wallet/import/compare`, {
      method: 'POST',
      body: formData,
    });

    if (!response.ok) {
      return toErrorResult<CompareResponse>(response.status, await readJson<unknown>(response));
    }

    return { ok: true, data: await readJson<CompareResponse>(response) };
  } catch {
    return { ok: false, error: { code: 'WALLET_UNAVAILABLE', message: 'Unable to reach the wallet API.' }, httpStatus: 503 };
  }
}

export async function compareCsv(files: File[], accountId: string): Promise<WalletResult<CsvCompareResponse>> {
  try {
    const formData = new FormData();
    for (const file of files) {
      formData.append('files', file);
    }
    formData.append('accountId', accountId);

    const response = await fetch(`${API_BASE_URL}/api/wallet/import/compare-csv`, {
      method: 'POST',
      body: formData,
    });

    if (!response.ok) {
      return toErrorResult<CsvCompareResponse>(response.status, await readJson<unknown>(response));
    }

    return { ok: true, data: await readJson<CsvCompareResponse>(response) };
  } catch {
    return { ok: false, error: { code: 'WALLET_UNAVAILABLE', message: 'Unable to reach the wallet API.' }, httpStatus: 503 };
  }
}

export async function submit(payload: SubmitRequest): Promise<WalletResult<SubmitResponse>> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/wallet/import/submit`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(payload),
    });

    if (!response.ok) {
      return toErrorResult<SubmitResponse>(response.status, await readJson<unknown>(response));
    }

    return { ok: true, data: await readJson<SubmitResponse>(response) };
  } catch {
    return { ok: false, error: { code: 'WALLET_UNAVAILABLE', message: 'Unable to reach the wallet API.' }, httpStatus: 503 };
  }
}
