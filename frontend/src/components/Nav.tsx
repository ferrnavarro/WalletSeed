import { NavLink } from 'react-router-dom';
import ProfileSwitcher from './ProfileSwitcher';

export default function Nav() {
  return (
    <header className="app-topbar">
      <div className="topbar-brand">
        <NavLink to="/" className="topbar-logo-link">
          <span className="topbar-logo-icon" aria-hidden="true">🌱</span>
          <span className="topbar-logo-text">WalletSeed</span>
        </NavLink>
      </div>

      <nav className="app-nav" aria-label="Primary">
        <div className="segmented-track">
          <NavLink
            to="/"
            className={({ isActive }) => `nav-link${isActive ? ' nav-link--active' : ''}`}
            end
          >
            <span className="nav-icon" aria-hidden="true">📄</span>
            <span>Statement Extract</span>
          </NavLink>
          <NavLink
            to="/wallet-import"
            className={({ isActive }) => `nav-link${isActive ? ' nav-link--active' : ''}`}
          >
            <span className="nav-icon" aria-hidden="true">💳</span>
            <span>Wallet Import</span>
          </NavLink>
          <NavLink
            to="/file-import"
            className={({ isActive }) => `nav-link${isActive ? ' nav-link--active' : ''}`}
          >
            <span className="nav-icon" aria-hidden="true">📁</span>
            <span>File Import</span>
          </NavLink>
        </div>
      </nav>

      <div className="topbar-actions">
        <ProfileSwitcher />
      </div>
    </header>
  );
}
