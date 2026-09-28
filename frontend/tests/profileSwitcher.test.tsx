import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import ProfileSwitcher from '../src/components/ProfileSwitcher';
import * as ProfileContextModule from '../src/context/ProfileContext';

describe('ProfileSwitcher', () => {
  it('renders select dropdown with current profile and switches on change', async () => {
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
        <ProfileSwitcher />
      </MemoryRouter>
    );

    const select = screen.getByRole('combobox', { name: /active profile/i });
    expect(select).toHaveValue('Fernando');

    await userEvent.selectOptions(select, 'Fatima');
    expect(selectProfileMock).toHaveBeenCalledWith('Fatima');
  });

  it('renders Select Profile button when no profile is selected', () => {
    vi.spyOn(ProfileContextModule, 'useProfile').mockReturnValue({
      profiles: [{ id: 'Fernando', name: 'Fernando' }],
      selectedProfile: null,
      selectProfile: vi.fn(),
      clearProfile: vi.fn(),
      loading: false,
    });

    render(
      <MemoryRouter>
        <ProfileSwitcher />
      </MemoryRouter>
    );

    expect(screen.getByRole('button', { name: /select profile/i })).toBeInTheDocument();
  });
});
