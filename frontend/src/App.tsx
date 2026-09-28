import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import Nav from './components/Nav';
import StatementExtractPage from './pages/StatementExtractPage';
import WalletImportPage from './pages/WalletImportPage';
import FileImportPage from './pages/FileImportPage';
import ProfileSelectPage from './pages/ProfileSelectPage';
import { ProfileProvider, useProfile } from './context/ProfileContext';

function RootPage() {
  const { selectedProfile } = useProfile();

  if (!selectedProfile) {
    return <ProfileSelectPage />;
  }

  return <StatementExtractPage />;
}

function RequireProfile({ children }: { children: React.ReactNode }) {
  const { selectedProfile, loading } = useProfile();
  const location = useLocation();

  if (loading) {
    return <div className="loading-container">Loading...</div>;
  }

  if (!selectedProfile) {
    return <Navigate to="/select-profile" state={{ from: location.pathname }} replace />;
  }

  return <>{children}</>;
}

export default function App() {
  return (
    <ProfileProvider>
      <BrowserRouter>
        <div className="app-container">
          <header className="app-header">
            <h1>WalletSeed</h1>
            <p className="app-subtitle">Statement extraction and wallet import workflows</p>
          </header>

          <Nav />

          <main className="app-content">
            <Routes>
              <Route path="/" element={<RootPage />} />
              <Route path="/select-profile" element={<ProfileSelectPage />} />
              <Route
                path="/wallet-import"
                element={
                  <RequireProfile>
                    <WalletImportPage />
                  </RequireProfile>
                }
              />
              <Route
                path="/file-import"
                element={
                  <RequireProfile>
                    <FileImportPage />
                  </RequireProfile>
                }
              />
            </Routes>
          </main>
        </div>
      </BrowserRouter>
    </ProfileProvider>
  );
}
