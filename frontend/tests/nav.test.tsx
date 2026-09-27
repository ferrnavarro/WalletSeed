import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Nav from '../src/components/Nav';

describe('Nav', () => {
  it('renders the statement extract and wallet import links with the correct hrefs', () => {
    render(
      <MemoryRouter initialEntries={['/']}>
        <Nav />
      </MemoryRouter>
    );

    expect(screen.getByRole('link', { name: /statement extract/i })).toHaveAttribute('href', '/');
    expect(screen.getByRole('link', { name: /wallet import/i })).toHaveAttribute('href', '/wallet-import');
  });

  it('marks the wallet import link as active for that route', () => {
    render(
      <MemoryRouter initialEntries={['/wallet-import']}>
        <Nav />
      </MemoryRouter>
    );

    expect(screen.getByRole('link', { name: /wallet import/i })).toHaveClass('nav-link--active');
  });
});
