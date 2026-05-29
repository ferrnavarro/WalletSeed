import { render, screen } from '@testing-library/react';
import TotalsPair from '../src/components/TotalsPair';

describe('TotalsPair', () => {
  it('renders both values without mismatch class when computed equals printed', () => {
    const { container } = render(
      <TotalsPair computed={125.50} printed={125.50} kind="charges" />
    );

    expect(screen.getByText('Charges')).toBeInTheDocument();
    expect(screen.getAllByText('$125.50')).toHaveLength(2);
    expect(container.firstChild).not.toHaveClass('totals-mismatch');
    expect(screen.queryByText(/mismatch/i)).not.toBeInTheDocument();
  });

  it('applies mismatch class and warning when values differ by more than 0.005', () => {
    const { container } = render(
      <TotalsPair computed={125.50} printed={125.55} kind="expense" />
    );

    expect(screen.getByText('Expense')).toBeInTheDocument();
    expect(screen.getByText('$125.50')).toBeInTheDocument();
    expect(screen.getByText('$125.55')).toBeInTheDocument();
    expect(container.firstChild).toHaveClass('totals-mismatch');
    expect(screen.getByText(/mismatch/i)).toBeInTheDocument();
  });

  it('renders only computed value and no mismatch when printed is null', () => {
    const { container } = render(
      <TotalsPair computed={125.50} printed={null} kind="income" />
    );

    expect(screen.getByText('Income')).toBeInTheDocument();
    expect(screen.getByText('$125.50')).toBeInTheDocument();
    expect(screen.queryByText(/printed/i)).not.toBeInTheDocument();
    expect(container.firstChild).not.toHaveClass('totals-mismatch');
  });
});
