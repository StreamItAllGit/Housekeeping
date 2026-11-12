// @module: Housekeeping.Web/components/ProfileSelect
// @purpose: Native <select> dropdown for fixed-vocabulary catalogs (HandBrake profiles, emulator runners) with stale/missing value protection.
// @depends-on: react
// @exports: ProfileSelect, SelectOptionItem
import React from 'react';

export interface SelectOptionItem {
  value: string;
  label?: string;
}

interface ProfileSelectProps {
  value: string;
  options: Array<string | SelectOptionItem>;
  onChange: (nextValue: string) => void;
  allowEmptyLabel?: string;
  title?: string;
  ariaLabel?: string;
  disabled?: boolean;
  className?: string;
  style?: React.CSSProperties;
}

export const ProfileSelect: React.FC<ProfileSelectProps> = ({
  value,
  options,
  onChange,
  allowEmptyLabel,
  title,
  ariaLabel,
  disabled,
  className,
  style,
}) => {
  const normalized: SelectOptionItem[] = options.map((opt) =>
    typeof opt === 'string' ? { value: opt, label: opt } : { value: opt.value, label: opt.label || opt.value }
  );

  const currentVal = value ?? '';
  const hasMatch =
    currentVal === ''
      ? Boolean(allowEmptyLabel) || normalized.some((o) => o.value === '')
      : normalized.some((o) => o.value.toLowerCase() === currentVal.toLowerCase());

  const resolvedValue = hasMatch
    ? normalized.find((o) => o.value.toLowerCase() === currentVal.toLowerCase())?.value ?? currentVal
    : currentVal;

  return (
    <select
      className={className ? `hk-select ${className}` : 'hk-select'}
      style={style}
      value={resolvedValue}
      disabled={disabled}
      title={title}
      aria-label={ariaLabel || title}
      onChange={(e) => onChange(e.target.value)}
    >
      {allowEmptyLabel !== undefined && <option value="">{allowEmptyLabel}</option>}
      {!hasMatch && currentVal !== '' && (
        <option value={currentVal}>⚠ Missing: {currentVal}</option>
      )}
      {normalized.map((opt) => (
        <option key={opt.value} value={opt.value}>
          {opt.label || opt.value}
        </option>
      ))}
    </select>
  );
};
