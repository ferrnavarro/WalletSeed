import { NavLink } from 'react-router-dom';

export default function Nav() {
  return (
    <nav className="app-nav" aria-label="Primary">
      <NavLink to="/" className={({ isActive }) => `nav-link${isActive ? ' nav-link--active' : ''}`} end>
        Statement Extract
      </NavLink>
      <NavLink to="/wallet-import" className={({ isActive }) => `nav-link${isActive ? ' nav-link--active' : ''}`}>
        Wallet Import
      </NavLink>
      <NavLink to="/csv-import" className={({ isActive }) => `nav-link${isActive ? ' nav-link--active' : ''}`}>
        CSV Import
      </NavLink>
    </nav>
  );
}
