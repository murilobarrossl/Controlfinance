export function SummaryItem({ label, value, tone = "default" }) {
  return (
    <div className="registry-summary__item">
      <dt>{label}</dt>
      <dd className={`registry-summary__value--${tone}`}>{value}</dd>
    </div>
  );
}

export function StatusFilter({ options, value, onChange }) {
  return (
    <div className="registry__segmented" role="group" aria-label="Filtrar por situação">
      {options.map((opt) => (
        <button
          key={opt.value || "all"}
          type="button"
          aria-pressed={value === opt.value}
          className={value === opt.value ? "registry__segment registry__segment--active" : "registry__segment"}
          onClick={() => onChange(opt.value)}
        >
          {opt.label}
        </button>
      ))}
    </div>
  );
}
