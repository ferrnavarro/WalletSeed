import type { Result, ExtractionErrorResponse } from '../types/api';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5080';

export async function extractStatement(file: File): Promise<Result> {
  const formData = new FormData();
  formData.append('file', file);

  try {
    const response = await fetch(`${API_BASE_URL}/api/statements/extract`, {
      method: 'POST',
      body: formData,
    });

    if (response.ok) {
      const data = await response.json();
      return { ok: true, data };
    } else {
      let errorBody: ExtractionErrorResponse['error'];
      try {
        const errorData: ExtractionErrorResponse = await response.json();
        errorBody = errorData.error;
      } catch {
        errorBody = {
          code: 'PARSE_FAILED',
          message: 'An unexpected response was received from the server.',
        };
      }
      return {
        ok: false,
        error: errorBody,
        httpStatus: response.status,
      };
    }
  } catch (err) {
    return {
      ok: false,
      error: {
        code: 'PARSE_FAILED',
        message: err instanceof Error ? err.message : 'Network connection failed.',
      },
      httpStatus: 500,
    };
  }
}
