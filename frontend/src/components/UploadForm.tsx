import React, { useState } from 'react';

interface UploadFormProps {
  onSubmit: (file: File) => void;
  // Stub for US3 preflight errors (can be empty / optional in US1)
  onLocalError?: (payload: { code: any; message: string }) => void;
}

export default function UploadForm({ onSubmit, onLocalError }: UploadFormProps) {
  const [file, setFile] = useState<File | null>(null);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      setFile(e.target.files[0]);
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (file) {
      // T074: Preflight validation
      const isPdf = file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf');
      if (!isPdf) {
        if (onLocalError) {
          onLocalError({ code: 'INVALID_FILE_TYPE', message: 'Please upload a PDF file.' });
        }
        return;
      }

      if (file.size > 25 * 1024 * 1024) {
        if (onLocalError) {
          onLocalError({ code: 'FILE_TOO_LARGE', message: 'This file exceeds the 25 MB limit.' });
        }
        return;
      }

      onSubmit(file);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="glass-card upload-form animate-fade-in">
      <h2>Upload Credit Card Statement</h2>
      <p className="form-description">Select a BAC Credomatic El Salvador credit card statement PDF to parse transactions.</p>
      
      <div className="file-input-container">
        <input 
          type="file" 
          id="statement-file"
          accept="application/pdf,.pdf" 
          onChange={handleFileChange}
          className="file-input"
        />
        <label htmlFor="statement-file" className="file-input-label">
          {file ? file.name : "Choose PDF Statement..."}
        </label>
      </div>

      <button 
        type="submit" 
        disabled={!file} 
        className="btn btn-primary btn-submit"
      >
        Extract Statement
      </button>
    </form>
  );
}
