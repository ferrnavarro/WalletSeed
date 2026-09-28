import React, { createContext, useContext, useEffect, useState, useMemo } from 'react';
import type { Profile } from '../types/profile';
import { listProfiles } from '../api/profilesClient';

export const PROFILE_STORAGE_KEY = 'wallet_seed_profile_id';

export interface ProfileContextValue {
  profiles: Profile[];
  selectedProfile: Profile | null;
  selectProfile: (profileId: string) => void;
  clearProfile: () => void;
  loading: boolean;
}

const ProfileContext = createContext<ProfileContextValue | undefined>(undefined);

export function ProfileProvider({ children }: { children: React.ReactNode }) {
  const [profiles, setProfiles] = useState<Profile[]>([]);
  const [selectedProfile, setSelectedProfile] = useState<Profile | null>(() => {
    try {
      const storedId = localStorage.getItem(PROFILE_STORAGE_KEY);
      return storedId ? { id: storedId, name: storedId } : null;
    } catch {
      return null;
    }
  });
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let isMounted = true;
    void (async () => {
      try {
        const data = await listProfiles();
        if (!isMounted) return;
        setProfiles(data);

        const storedId = localStorage.getItem(PROFILE_STORAGE_KEY);
        if (storedId) {
          const match = data.find((p) => p.id.toLowerCase() === storedId.toLowerCase());
          if (match) {
            setSelectedProfile(match);
          }
        }
      } finally {
        if (isMounted) setLoading(false);
      }
    })();

    return () => {
      isMounted = false;
    };
  }, []);

  const selectProfile = (profileId: string) => {
    const match = profiles.find((p) => p.id.toLowerCase() === profileId.toLowerCase());
    if (match) {
      setSelectedProfile(match);
      localStorage.setItem(PROFILE_STORAGE_KEY, match.id);
    }
  };

  const clearProfile = () => {
    setSelectedProfile(null);
    localStorage.removeItem(PROFILE_STORAGE_KEY);
  };

  const value = useMemo(
    () => ({ profiles, selectedProfile, selectProfile, clearProfile, loading }),
    [profiles, selectedProfile, loading]
  );

  return <ProfileContext.Provider value={value}>{children}</ProfileContext.Provider>;
}

export function useProfile() {
  const context = useContext(ProfileContext);
  return (
    context ?? {
      profiles: [],
      selectedProfile: null,
      selectProfile: () => {},
      clearProfile: () => {},
      loading: false,
    }
  );
}
