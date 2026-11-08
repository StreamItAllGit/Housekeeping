import React, { useId } from 'react';

interface ComboSelectProps {
  value: string;
  onChange: (next: string) => void;
  options: string[];
  placeholder?: string;
  mono?: boolean;
  ariaLabel?: string;
}

/**
 * Searchable + custom-value-creatable combo input using native HTML <datalist>,
 * guaranteeing zero vertical glyph clipping and instant single-click dropdown access.
 */
export const ComboSelect: React.FC<ComboSelectProps> = ({
  value,
  onChange,
  options,
  placeholder,
  mono,
  ariaLabel,
}) => {
  const listId = useId();
  return (
    <>
      <input
        type="text"
        list={listId}
        className={`hk-input ${mono ? 'mono' : ''}`.trim()}
        value={value ?? ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label={ariaLabel || placeholder || 'Value'}
      />
      <datalist id={listId}>
        {options.map((opt) => (
          <option key={opt} value={opt} />
        ))}
      </datalist>
    </>
  );
};
