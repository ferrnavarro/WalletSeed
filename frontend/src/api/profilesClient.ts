import type { Profile } from '../types/profile';

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5080';

export async function listProfiles(): Promise<Profile[]> {
  try {
    const response = await fetch(`${API_BASE_URL}/api/profiles`, {
      method: 'GET',
      headers: { Accept: 'application/json' },
    });

    if (!response.ok) {
      throw new Error(`Failed to fetch profiles: ${response.status}`);
    }

    return (await response.json()) as Profile[];
  } catch {
    return [];
  }
}
