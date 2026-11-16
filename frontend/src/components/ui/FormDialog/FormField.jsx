import CurrencyInput from "../CurrencyInput/CurrencyInput.jsx";
import DateInput from "../DateInput/DateInput.jsx";
import { FIELD_EXAMPLES, fieldExample } from "../../../utils/fieldExamples.js";

export function FormField({ label, full = false, help, helpId, children }) {
  return (
    <label className={`form-dialog__field${full ? " form-dialog__field--full" : ""}`}>
      <span className="form-dialog__label">{label}</span>
      {children}
      {help && (
        <span id={helpId} className="form-dialog__help">
          {help}
        </span>
      )}
    </label>
  );
}

export function TextField({ label, example, full, help, helpId, ...inputProps }) {
  return (
    <FormField label={label} full={full} help={help} helpId={helpId}>
      <input type="text" autoComplete="off" placeholder={fieldExample(example)} aria-describedby={help ? helpId : undefined} {...inputProps} />
    </FormField>
  );
}

export function MoneyField({ label, full, value, onChange, required }) {
  return (
    <FormField label={label} full={full}>
      <CurrencyInput value={value} onChange={onChange} placeholder={FIELD_EXAMPLES.money} required={required} />
    </FormField>
  );
}

export function DateField({ label, full, ...dateProps }) {
  return (
    <FormField label={label} full={full}>
      <DateInput placeholder={FIELD_EXAMPLES.date} {...dateProps} />
    </FormField>
  );
}
