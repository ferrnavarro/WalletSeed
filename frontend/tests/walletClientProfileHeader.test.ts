import { afterEach, describe, expect, it, vi } from 'vitest';
import { getProfileHeaders, listAccounts } from '../src/api/walletClient';

describe('walletClient profile headers', () => {
  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('returns empty headers when no profile is in localStorage', () => {
    localStorage.removeItem('wallet_seed_profile_id');
    expect(getProfileHeaders()).toEqual({});
  });

  it('returns X-Profile header when profile is in localStorage', () => {
    localStorage.setItem('wallet_seed_profile_id', 'Fatima');
    expect(getProfileHeaders()).toEqual({ 'X-Profile': 'Fatima' });
  });

  it('includes X-Profile header in outgoing fetch calls', async () => {
    localStorage.setItem('wallet_seed_profile_id', 'Fernando');

    let capturedHeaders: HeadersInit | undefined;
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input, init) => {
      capturedHeaders = init?.headers;
      return new Response(JSON.stringify({ accounts: [] }), { status: 200 });
    });

    const result = await listAccounts();
    expect(result.ok).toBe(true);
    expect(capturedHeaders).toMatchObject({
      Accept: 'application/json',
      'X-Profile': 'Fernando',
    });
  });
});
