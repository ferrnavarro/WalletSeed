import { useNavigate, useLocation } from 'react-router-dom';
import { useProfile } from '../context/ProfileContext';

export default function ProfileSelectPage() {
  const { profiles, selectProfile, loading, selectedProfile } = useProfile();
  const navigate = useNavigate();
  const location = useLocation();

  const handleSelect = (profileId: string) => {
    selectProfile(profileId);
    const destination = (location.state as { from?: string } | null)?.from || '/file-import';
    navigate(destination);
  };

  if (loading) {
    return (
      <div className="profile-select-container">
        <p className="profile-select-loading">Loading profiles...</p>
      </div>
    );
  }

  return (
    <div className="profile-select-container">
      <div className="profile-select-header">
        <h2>Select Profile</h2>
        <p className="profile-select-subtitle">
          Choose a profile to configure the BudgetBakers Wallet API session.
        </p>
      </div>

      {profiles.length === 0 ? (
        <div className="profile-empty-warning">
          <p>No profiles configured in <code>appsettings.json</code>.</p>
        </div>
      ) : (
        <div className="profile-cards-grid">
          {profiles.map((profile) => {
            const isCurrent = selectedProfile?.id.toLowerCase() === profile.id.toLowerCase();
            const initial = profile.name ? profile.name.charAt(0).toUpperCase() : profile.id.charAt(0).toUpperCase();

            return (
              <div
                key={profile.id}
                className={`profile-card${isCurrent ? ' profile-card--active' : ''}`}
                onClick={() => handleSelect(profile.id)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    handleSelect(profile.id);
                  }
                }}
              >
                <div className="profile-avatar">
                  <span>{initial}</span>
                </div>
                <h3 className="profile-card-name">{profile.name}</h3>
                {isCurrent && <span className="profile-badge-current">Active</span>}
                <button
                  type="button"
                  className="btn btn--primary profile-select-btn"
                  onClick={(e) => {
                    e.stopPropagation();
                    handleSelect(profile.id);
                  }}
                >
                  {isCurrent ? 'Continue as ' + profile.name : 'Select Profile'}
                </button>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
