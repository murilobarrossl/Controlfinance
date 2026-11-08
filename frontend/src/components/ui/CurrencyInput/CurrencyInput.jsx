import "./CurrencyInput.css";

const MAX_DIGITS = 13;

const formatter = new Intl.NumberFormat("pt-BR", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

function toDisplay(value) {
  if (value === "" || value === null || value === undefined) return "";
  const number = Number(value);
  return Number.isFinite(number) ? formatter.format(number) : "";
}

export default function CurrencyInput({ value, onChange, placeholder, required = false }) {
  const handleChange = (e) => {
    const digits = e.target.value.replace(/\D/g, "").replace(/^0+/, "").slice(0, MAX_DIGITS);
    const next = digits ? (Number(digits) / 100).toFixed(2) : "";
    onChange({ target: { value: next } });
  };

  return (
    <div className="currency-input">
      <span className="currency-input__prefix">R$</span>
      <input
        type="text"
        inputMode="numeric"
        autoComplete="off"
        className="currency-input__field"
        placeholder={placeholder}
        value={toDisplay(value)}
        onChange={handleChange}
        required={required}
      />
    </div>
  );
}
