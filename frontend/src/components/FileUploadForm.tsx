import React, { useState } from 'react';

interface FileUploadFormProps {
  onSubmit: (files: File[]) => void;
  onLocalError?: (payload: { code: string; message: string }) => void;
}

const MAX_BYTES = 25 * 1024 * 1024;
const ACCEPTED_EXTENSIONS = ['.csv', '.xlsx'];

function isAccepted(file: File): boolean {
  const name = file.name.toLowerCase();
  return ACCEPTED_EXTENSIONS.some((ext) => name.endsWith(ext));
}

export default function FileUploadForm({ onSubmit, onLocalError }: FileUploadFormProps) {
  const [files, setFiles] = useState<File[]>([]);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      setFiles(Array.from(e.target.files));
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (files.length === 0) {
      return;
    }

    for (const file of files) {
      if (!isAccepted(file)) {
        onLocalError?.({ code: 'INVALID_FILE_TYPE', message: `"${file.name}" is not a supported file (CSV or Excel).` });
        return;
      }

      if (file.size > MAX_BYTES) {
        onLocalError?.({ code: 'FILE_TOO_LARGE', message: `"${file.name}" exceeds the 25 MB limit.` });
        return;
      }
    }

    onSubmit(files);
  };

  return (
    <form onSubmit={handleSubmit} className="glass-card upload-form animate-fade-in">
      <h2>Upload Bank Statement Files</h2>
      <p className="form-description">Select BAC CSV exports or Excel (.xlsx) statements from Promerica or Banco Cuscatlán (credit card and bank account exports) to compare against your Wallet records.</p>

      <div className="file-input-container">
        <input
          type="file"
          id="statement-files"
          accept=".csv,.xlsx,text/csv"
          multiple
          onChange={handleFileChange}
          className="file-input"
        />
        <label htmlFor="statement-files" className="file-input-label">
          {files.length > 0 ? `${files.length} file(s) selected` : 'Choose CSV or Excel files...'}
        </label>
      </div>

      {files.length > 0 ? (
        <ul className="form-description" style={{ margin: '0.5rem 0 0', paddingLeft: '1.25rem' }}>
          {files.map((file) => (
            <li key={file.name}>{file.name}</li>
          ))}
        </ul>
      ) : null}

      <button
        type="submit"
        disabled={files.length === 0}
        className="btn btn-primary btn-submit"
      >
        Compare with Wallet
      </button>
    </form>
  );
}
