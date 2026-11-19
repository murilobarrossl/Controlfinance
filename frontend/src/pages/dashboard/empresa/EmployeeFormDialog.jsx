import { useState } from "react";
import FormDialog from "../../../components/ui/FormDialog/FormDialog.jsx";
import { DateField, MoneyField, TextField } from "../../../components/ui/FormDialog/FormField.jsx";
import { addEmployeePaymentMethod, createEmployee, updateEmployee } from "../../../api/employees.js";
import { isValidCpf } from "../../../utils/authRules.js";
import { applyMask, CPF_MASK } from "../../../utils/masks.js";
import { PaymentChoice } from "./PaymentFields.jsx";
import { EMPTY_BANK, EMPTY_PIX, paymentFailureMessage, paymentProblem, savePaymentChoice } from "./payeePayment.js";

const digitsOf = (value) => value.replace(/\D/g, "");

export default function EmployeeFormDialog({ employee, onClose, onSaved }) {
  const isEdit = Boolean(employee);
  const [form, setForm] = useState(() => ({
    fullName: employee?.fullName ?? "",
    cpf: "",
    role: employee?.role ?? "",
    grossSalary: employee ? String(employee.grossSalary) : "",
    admissionDate: employee?.admissionDate ?? "",
  }));
  const [payment, setPayment] = useState("later");
  const [pix, setPix] = useState(EMPTY_PIX);
  const [bank, setBank] = useState(EMPTY_BANK);
  const [created, setCreated] = useState(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  const set = (field) => (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }));
  const coreLocked = Boolean(created);

  function handleCpf(e) {
    const digits = digitsOf(e.target.value).slice(0, 11);
    setForm((prev) => ({ ...prev, cpf: applyMask(digits, CPF_MASK, digitsOf(prev.cpf)) }));
  }

  function validate() {
    if (!coreLocked) {
      if (form.fullName.trim().split(/\s+/).length < 2) return "Informe o nome completo.";
      const cpf = digitsOf(form.cpf);
      if ((!isEdit || cpf.length > 0) && !isValidCpf(cpf)) return "CPF inválido. Confira os números digitados.";
      if (!form.role.trim()) return "Informe o cargo ou função.";
      if (!(Number(form.grossSalary) > 0)) return "Informe o salário bruto mensal.";
      if (!form.admissionDate) return "Informe a data de admissão no formato dd/mm/aaaa.";
    }
    return paymentProblem(payment, pix, bank);
  }

  async function handleSubmit(e) {
    e.preventDefault();
    const problem = validate();
    if (problem) {
      setError(problem);
      return;
    }
    setError("");
    setSubmitting(true);

    if (isEdit) {
      try {
        onSaved(
          await updateEmployee(employee.id, {
            fullName: form.fullName.trim(),
            cpf: digitsOf(form.cpf) || null,
            role: form.role.trim(),
            grossSalary: Number(form.grossSalary),
            admissionDate: form.admissionDate,
          })
        );
      } catch (err) {
        setError(err.message || "Não foi possível salvar o funcionário.");
        setSubmitting(false);
      }
      return;
    }

    let saved = created;
    if (!saved) {
      try {
        saved = await createEmployee({
          fullName: form.fullName.trim(),
          cpf: digitsOf(form.cpf),
          role: form.role.trim(),
          grossSalary: Number(form.grossSalary),
          admissionDate: form.admissionDate,
        });
      } catch (err) {
        setError(err.message || "Não foi possível cadastrar o funcionário.");
        setSubmitting(false);
        return;
      }
    }

    try {
      const withPayment = await savePaymentChoice(payment, { pix, bank }, (payload) => addEmployeePaymentMethod(saved.id, payload));
      onSaved(withPayment ?? saved);
    } catch (err) {
      setCreated(saved);
      setError(paymentFailureMessage("Funcionário", payment, err));
      setSubmitting(false);
    }
  }

  const close = created ? () => onSaved(created) : onClose;

  const submitLabel = isEdit ? "Salvar" : created ? (payment === "later" ? "Concluir" : "Salvar pagamento") : "Cadastrar";

  return (
    <FormDialog
      open
      title={isEdit ? "Editar dados principais" : "Cadastrar funcionário"}
      error={error}
      submitLabel={submitLabel}
      cancelLabel={created ? "Configurar depois" : "Cancelar"}
      submitting={submitting}
      onSubmit={handleSubmit}
      onClose={close}
    >
      <fieldset className="form-dialog__section" disabled={coreLocked}>
        {!isEdit && <legend className="form-dialog__section-title">Dados do funcionário</legend>}
        <div className="form-dialog__grid">
          <TextField
            label="Nome completo"
            example="personName"
            full
            value={form.fullName}
            onChange={set("fullName")}
            maxLength={150}
            required
          />
          <TextField
            label="CPF"
            example={isEdit ? employee.cpfMasked : "cpf"}
            help={isEdit ? "Em branco mantém o atual." : undefined}
            helpId="employee-cpf-keep"
            inputMode="numeric"
            value={form.cpf}
            onChange={handleCpf}
            maxLength={CPF_MASK.length}
            required={!isEdit}
          />
          <DateField
            label="Data de admissão"
            value={form.admissionDate}
            onChange={(admissionDate) => setForm((prev) => ({ ...prev, admissionDate }))}
            required
          />
          <TextField label="Cargo ou função" example="role" value={form.role} onChange={set("role")} maxLength={100} required />
          <MoneyField label="Salário bruto mensal" value={form.grossSalary} onChange={set("grossSalary")} required />
        </div>
      </fieldset>

      {!isEdit && (
        <PaymentChoice
          payment={payment}
          onPaymentChange={(value) => {
            setPayment(value);
            setError("");
          }}
          pix={pix}
          onPixChange={setPix}
          bank={bank}
          onBankChange={setBank}
          idPrefix="new-employee-pix"
        />
      )}
    </FormDialog>
  );
}
