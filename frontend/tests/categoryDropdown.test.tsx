import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import CategoryDropdown from '../src/components/CategoryDropdown';

describe('CategoryDropdown', () => {
  it('renders categories, uses the placeholder, and reports the selected id', async () => {
    const onChange = vi.fn();
    render(
      <CategoryDropdown
        label="Category"
        categories={[
          { id: 'cat-1', name: 'Food', color: null },
          { id: 'cat-2', name: 'Transport', color: null },
        ]}
        value={null}
        onChange={onChange}
      />,
    );

    expect(screen.getByRole('combobox', { name: /category/i })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /pick a category/i })).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /category/i }), 'cat-2');

    expect(onChange).toHaveBeenCalledWith('cat-2');
  });
});
