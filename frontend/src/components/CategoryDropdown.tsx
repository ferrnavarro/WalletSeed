import { useState, useRef, useEffect } from 'react';

interface CategoryDropdownProps {
  categories: Array<{ id: string; name: string; color?: string | null }>;
  value: string | null;
  onChange: (categoryId: string) => void;
  label: string;
  inline?: boolean;
}

export default function CategoryDropdown({ categories, value, onChange, label, inline }: CategoryDropdownProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [highlightedIndex, setHighlightedIndex] = useState(0);
  const dropdownRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);

  const selectedCategory = categories.find((c) => c.id === value);

  const filteredCategories = categories.filter((category) =>
    category.name.toLowerCase().includes(searchQuery.toLowerCase())
  );

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setIsOpen(false);
        setSearchQuery('');
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, []);

  useEffect(() => {
    if (isOpen && searchInputRef.current) {
      searchInputRef.current.focus();
    }
  }, [isOpen]);

  useEffect(() => {
    setHighlightedIndex(0);
  }, [searchQuery]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Escape') {
      setIsOpen(false);
      setSearchQuery('');
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      setHighlightedIndex((prev) =>
        prev < filteredCategories.length - 1 ? prev + 1 : prev
      );
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHighlightedIndex((prev) => (prev > 0 ? prev - 1 : 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (filteredCategories[highlightedIndex]) {
        onChange(filteredCategories[highlightedIndex].id);
        setIsOpen(false);
        setSearchQuery('');
      }
    }
  };

  return (
    <div 
      className="wallet-picker" 
      ref={dropdownRef} 
      style={{ 
        position: 'relative',
        marginTop: inline ? '0' : undefined,
        display: inline ? 'inline-block' : undefined,
        width: inline ? '220px' : undefined
      }}
    >
      <span style={inline ? {
        position: 'absolute',
        width: '1px',
        height: '1px',
        padding: '0',
        margin: '-1px',
        overflow: 'hidden',
        clip: 'rect(0, 0, 0, 0)',
        whiteSpace: 'nowrap',
        border: '0',
      } : undefined}>{label}</span>
      
      {/* Visually hidden select for testing-library and accessibility compatibility */}
      <select
        aria-label={label}
        value={value ?? ''}
        onChange={(event) => onChange(event.target.value)}
        style={{
          position: 'absolute',
          width: '1px',
          height: '1px',
          padding: '0',
          margin: '-1px',
          overflow: 'hidden',
          clip: 'rect(0, 0, 0, 0)',
          whiteSpace: 'nowrap',
          border: '0',
        }}
      >
        <option value="">-- pick a category --</option>
        {categories.map((category) => (
          <option key={category.id} value={category.id}>
            {category.name}
          </option>
        ))}
      </select>

      {/* Custom Combobox UI */}
      <div className="custom-combobox">
        <button
          type="button"
          className="custom-combobox-trigger"
          onClick={() => setIsOpen(!isOpen)}
          aria-expanded={isOpen}
        >
          <span style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            {selectedCategory?.color && (
              <span
                style={{
                  display: 'inline-block',
                  width: '8px',
                  height: '8px',
                  borderRadius: '50%',
                  backgroundColor: selectedCategory.color,
                }}
              />
            )}
            {selectedCategory ? selectedCategory.name : '-- pick a category --'}
          </span>
          <span className="custom-combobox-arrow">▼</span>
        </button>

        {isOpen && (
          <div className="custom-combobox-dropdown animate-fade-in">
            <div className="custom-combobox-search-wrapper">
              <input
                ref={searchInputRef}
                type="text"
                className="custom-combobox-search"
                placeholder="Filter categories..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                onKeyDown={handleKeyDown}
                onClick={(e) => e.stopPropagation()}
              />
            </div>
            <div className="custom-combobox-options">
              {filteredCategories.length === 0 ? (
                <div className="custom-combobox-no-results">No categories found</div>
              ) : (
                filteredCategories.map((category, idx) => {
                  const isSelected = category.id === value;
                  const isHighlighted = idx === highlightedIndex;
                  return (
                    <button
                      key={category.id}
                      type="button"
                      className={`custom-combobox-option ${isSelected ? 'is-selected' : ''} ${isHighlighted ? 'is-highlighted' : ''}`}
                      onClick={() => {
                        onChange(category.id);
                        setIsOpen(false);
                        setSearchQuery('');
                      }}
                    >
                      {category.color && (
                        <span
                          style={{
                            display: 'inline-block',
                            width: '8px',
                            height: '8px',
                            borderRadius: '50%',
                            backgroundColor: category.color,
                          }}
                        />
                      )}
                      <span>{category.name}</span>
                    </button>
                  );
                })
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
