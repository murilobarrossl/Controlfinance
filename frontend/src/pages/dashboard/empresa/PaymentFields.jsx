import { applyMask, CNPJ_MASK, CPF_MASK } from "../../../utils/masks.js";
import { PAYEE_ACCOUNT_TYPES, PIX_KEY_TYPES } from "../../../utils/employees.js";
import { FIELD_EXAMPLES, PIX_KEY_EXAMPLES } from "../../../utils/fieldExamples.js";
import { TextField } from "../../../components/ui/FormDialog/FormField.jsx";
import { PAYMENT_OPTIONS } from "./payeePayment.js";

const digitsOf = (value) => value.replace(/\D/g, "");

export function PixFields({ value, onChange, idPrefix = "pix", disabled = false }) {
  function handleKey(e) {
    const raw = e.target.value;
    let key = raw;
    if (value.type === "Cpf") key = applyMask(digitsOf(raw).slice(0, 11), CPF_MASK, digitsOf(value.key));
    else if (value.type === "Cnpj") key = applyMask(digitsOf(raw).slice(0, 14), CNPJ_MASK, digitsOf(value.key));
    onChange({ ...value, key });
  }

  return (
    <div className="form-dialog__grid form-dialog__grid--pix">
      <label className="form-dialog__field">
        <span className="form-dialog__label">Tipo de chave</span>
        <select
          id={`${idPrefix}-type`}
          value={value.type}
          disabled={disabled}
          onChange={(e) => onChange({ type: e.target.value, key: "" })}
        >
          {PIX_KEY_TYPES.map((t) => (
            <option key={t.value} value={t.value}>
              {t.label}
            </option>
          ))}
        </select>
      </label>
      <TextField
        label="Chave"
        example={PIX_KEY_EXAMPLES[value.type]}
        id={`${idPrefix}-key`}
        inputMode={value.type === "Email" || value.type === "Random" ? "text" : "numeric"}
        value={value.key}
        onChange={handleKey}
        disabled={disabled}
        required
      />
    </div>
  );
}

export function BankFields({ value, onChange, accountPlaceholder = FIELD_EXAMPLES.account, disabled = false }) {
  const set = (field) => (e) => onChange({ ...value, [field]: e.target.value });

  return (
    <div className="form-dialog__grid">
      <TextField
        label="Banco"
        example="bankCode"
        inputMode="numeric"
        value={value.bankCode}
        onChange={(e) => onChange({ ...value, bankCode: digitsOf(e.target.value).slice(0, 3) })}
        disabled={disabled}
        required
      />
      <label className="form-dialog__field">
        <span className="form-dialog__label">Tipo de conta</span>
        <select value={value.accountType} onChange={set("accountType")} disabled={disabled}>
          {PAYEE_ACCOUNT_TYPES.map((t) => (
            <option key={t.value} value={t.value}>
              {t.label}
            </option>
          ))}
        </select>
      </label>
      <TextField
        label="Agência"
        example="branch"
        inputMode="numeric"
        value={value.branch}
        onChange={set("branch")}
        maxLength={6}
        disabled={disabled}
        required
      />
      <TextField
        label="Conta com dígito"
        example={accountPlaceholder}
        inputMode="numeric"
        value={value.account}
        onChange={set("account")}
        maxLength={22}
        disabled={disabled}
        required
      />
    </div>
  );
}

export function PaymentChoice({ payment, onPaymentChange, pix, onPixChange, bank, onBankChange, idPrefix }) {
  return (
    <fieldset className="form-dialog__section form-dialog__section--divided">
      <legend className="form-dialog__section-title">
        Dados para pagamento <span className="form-dialog__section-note">Opcional</span>
      </legend>

      <div className="form-dialog__segmented" role="radiogroup" aria-label="Forma de pagamento">
        {PAYMENT_OPTIONS.map((option) => (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={payment === option.value}
            onClick={() => onPaymentChange(option.value)}
          >
            {option.label}
          </button>
        ))}
      </div>

      {payment === "pix" && <PixFields value={pix} onChange={onPixChange} idPrefix={idPrefix} />}
      {payment === "bank" && <BankFields value={bank} onChange={onBankChange} />}
    </fieldset>
  );
}
