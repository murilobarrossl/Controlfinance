import "./CurrencyInput.css";

export default function CurrencyInput({
  value,
  onChange,
  placeholder,
  required = false,
  min = "0",
  step = "0.01",
}) {
  return (
    <div className="currency-input">
      <span className="currency-input__prefix">R$</span>
      <input
        type="number"
        className="currency-input__field"
        placeholder={placeholder}
        min={min}
        step={step}
        value={value}
        onChange={onChange}
        required={required}
      />
    </div>
  );
}
