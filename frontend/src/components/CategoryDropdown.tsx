interface CategoryDropdownProps {
  categories: Array<{ id: string; name: string }>;
  value: string | null;
  onChange: (categoryId: string) => void;
  label: string;
}

export default function CategoryDropdown({ categories, value, onChange, label }: CategoryDropdownProps) {
  return (
    <label className="wallet-picker">
      <span>{label}</span>
      <select aria-label={label} value={value ?? ''} onChange={(event) => onChange(event.target.value)}>
        <option value="">-- pick a category --</option>
        {categories.map((category) => (
          <option key={category.id} value={category.id}>
            {category.name}
          </option>
        ))}
      </select>
    </label>
  );
}
