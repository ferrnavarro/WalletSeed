import type { ExtractionErrorResponse } from '../types/api';

interface ErrorBannerProps {
  error: ExtractionErrorResponse['error'];
  httpStatus: number;
}

const ERROR_MESSAGE_MAP: Record<string, string> = {
  INVALID_FILE_TYPE: "Please upload a PDF file.",
  EMPTY_FILE: "The selected file is empty.",
  FILE_TOO_LARGE: "This file exceeds the 25 MB limit.",
  PASSWORD_PROTECTED: "This PDF is password-protected. Please remove the password and try again.",
  NO_TEXT_EXTRACTABLE: "This PDF doesn't contain machine-readable text. Scanned PDFs aren't supported in this version.",
  UNRECOGNIZED_LAYOUT: "We couldn't recognize this as a BAC Credomatic statement.",
  PARSE_FAILED: "Something went wrong while reading this PDF. Please try again."
};

export default function ErrorBanner({ error, httpStatus }: ErrorBannerProps) {
  const userMessage = ERROR_MESSAGE_MAP[error.code] || error.message;

  return (
    <div className="error-banner glass-card" id="error-banner">
      <div className="error-icon">⚠️</div>
      <div className="error-content">
        <h3 className="error-title">Extraction Failed</h3>
        <p className="error-message">{userMessage}</p>
        <span className="error-meta">Code: {error.code} | HTTP {httpStatus}</span>
      </div>
    </div>
  );
}
