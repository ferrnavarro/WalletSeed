import { BrowserRouter, Routes, Route } from 'react-router-dom';
import Nav from './components/Nav';
import StatementExtractPage from './pages/StatementExtractPage';
import WalletImportPage from './pages/WalletImportPage';
import CsvWalletImportPage from './pages/CsvWalletImportPage';

export default function App() {
  return (
    <BrowserRouter>
      <div className="app-container">
        <header className="app-header">
          <h1>WalletSeed</h1>
          <p className="app-subtitle">Statement extraction and wallet import workflows</p>
        </header>

        <Nav />

        <main className="app-content">
          <Routes>
            <Route path="/" element={<StatementExtractPage />} />
            <Route path="/wallet-import" element={<WalletImportPage />} />
            <Route path="/csv-import" element={<CsvWalletImportPage />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}
