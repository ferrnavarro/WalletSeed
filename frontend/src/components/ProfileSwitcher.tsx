import { useNavigate } from 'react-router-dom';
import { useProfile } from '../context/ProfileContext';

export default function ProfileSwitcher() {
  const { profiles, selectedProfile, selectProfile } = useProfile();
  const navigate = useNavigate();

  if (!selectedProfile) {
    return (
      <div className="profile-switcher profile-switcher--empty">
        <button
          type="button"
          className="btn btn--secondary btn--small"
          onClick={() => navigate('/select-profile')}
        >
          Select Profile
        </button>
      </div>
    );
  }

  const initial = selectedProfile.name
    ? selectedProfile.name.charAt(0).toUpperCase()
    : selectedProfile.id.charAt(0).toUpperCase();

  return (
    <div className="profile-switcher" aria-label="Profile Switcher">
      <span className="profile-avatar-mini" aria-hidden="true">
        {initial}
      </span>
      <div className="profile-switcher-select-wrap">
        <select
          className="profile-switcher-select"
          value={selectedProfile.id}
          onChange={(e) => selectProfile(e.target.value)}
          aria-label="Active profile"
        >
          {profiles.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </select>
        <span className="profile-switcher-chevron" aria-hidden="true">▾</span>
      </div>
    </div>
  );
}
