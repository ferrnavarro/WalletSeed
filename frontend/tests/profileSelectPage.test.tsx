import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import ProfileSelectPage from '../src/pages/ProfileSelectPage';
import * as ProfileContextModule from '../src/context/ProfileContext';

describe('ProfileSelectPage', () => {
  it('renders configured profiles and allows selecting one', async () => {
    const selectProfileMock = vi.fn();

    vi.spyOn(ProfileContextModule, 'useProfile').mockReturnValue({
      profiles: [
        { id: 'Fernando', name: 'Fernando' },
        { id: 'Fatima', name: 'Fatima' },
      ],
      selectedProfile: { id: 'Fernando', name: 'Fernando' },
      selectProfile: selectProfileMock,
      clearProfile: vi.fn(),
      loading: false,
    });

    render(
      <MemoryRouter>
        <ProfileSelectPage />
      </MemoryRouter>
    );

    expect(screen.getByRole('heading', { name: /select profile/i })).toBeInTheDocument();
    expect(screen.getByText('Fernando')).toBeInTheDocument();
    expect(screen.getByText('Fatima')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();

    const fatimaCard = screen.getByText('Fatima').closest('.profile-card');
    expect(fatimaCard).toBeInTheDocument();
    await userEvent.click(fatimaCard!);

    expect(selectProfileMock).toHaveBeenCalledWith('Fatima');
  });

  it('renders loading indicator when profiles are loading', () => {
    vi.spyOn(ProfileContextModule, 'useProfile').mockReturnValue({
      profiles: [],
      selectedProfile: null,
      selectProfile: vi.fn(),
      clearProfile: vi.fn(),
      loading: true,
    });

    render(
      <MemoryRouter>
        <ProfileSelectPage />
      </MemoryRouter>
    );

    expect(screen.getByText('Loading profiles...')).toBeInTheDocument();
  });
});
