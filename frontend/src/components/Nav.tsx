import { NavLink } from 'react-router-dom';
import ProfileSwitcher from './ProfileSwitcher';

export default function Nav() {
  return (
    <div className="app-nav-wrapper">
      <nav className="app-nav" aria-label="Primary">
        <NavLink to="/" className={({ isActive }) => `nav-link${isActive ? ' nav-link--active' : ''}`} end>
          Statement Extract
        </NavLink>
        <NavLink to="/wallet-import" className={({ isActive }) => `nav-link${isActive ? ' nav-link--active' : ''}`}>
          Wallet Import
        </NavLink>
        <NavLink to="/file-import" className={({ isActive }) => `nav-link${isActive ? ' nav-link--active' : ''}`}>
          File Import
        </NavLink>
      </nav>
      <ProfileSwitcher />
    </div>
  );
}
