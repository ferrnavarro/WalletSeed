import React, { useState } from 'react';

interface CsvUploadFormProps {
  onSubmit: (files: File[]) => void;
  onLocalError?: (payload: { code: string; message: string }) => void;
}

const MAX_BYTES = 25 * 1024 * 1024;

export default function CsvUploadForm({ onSubmit, onLocalError }: CsvUploadFormProps) {
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
      const isCsv = file.type === 'text/csv' || file.name.toLowerCase().endsWith('.csv');
      if (!isCsv) {
        onLocalError?.({ code: 'INVALID_FILE_TYPE', message: `"${file.name}" is not a CSV file.` });
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
      <h2>Upload BAC CSV Statements</h2>
      <p className="form-description">Select one or more BAC Credomatic CSV exports to compare against your Wallet records.</p>

      <div className="file-input-container">
        <input
          type="file"
          id="csv-files"
          accept=".csv,text/csv"
          multiple
          onChange={handleFileChange}
          className="file-input"
        />
        <label htmlFor="csv-files" className="file-input-label">
          {files.length > 0 ? `${files.length} file(s) selected` : 'Choose CSV files...'}
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
