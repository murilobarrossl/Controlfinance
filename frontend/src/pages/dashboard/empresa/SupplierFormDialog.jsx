import { useState } from "react";
import FormDialog from "../../../components/ui/FormDialog/FormDialog.jsx";
import { TextField } from "../../../components/ui/FormDialog/FormField.jsx";
import { addSupplierPaymentMethod, createSupplier, updateSupplier } from "../../../api/suppliers.js";
import { isValidCnpj, isValidCpf } from "../../../utils/authRules.js";
import { applyMask, CNPJ_MASK, getCpfCnpjMask } from "../../../utils/masks.js";
import { PaymentChoice } from "./PaymentFields.jsx";
import { EMPTY_BANK, EMPTY_PIX, paymentFailureMessage, paymentProblem, savePaymentChoice } from "./payeePayment.js";

const digitsOf = (value) => value.replace(/\D/g, "");

export default function SupplierFormDialog({ supplier, categories = [], onClose, onSaved }) {
  const isEdit = Boolean(supplier);
  const [form, setForm] = useState(() => ({
    legalName: supplier?.legalName ?? "",
    document: "",
    tradeName: supplier?.tradeName ?? "",
    category: supplier?.category ?? "",
  }));
  const [payment, setPayment] = useState("later");
  const [pix, setPix] = useState(EMPTY_PIX);
  const [bank, setBank] = useState(EMPTY_BANK);
  const [created, setCreated] = useState(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  const set = (field) => (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }));
  const coreLocked = Boolean(created);

  function handleDocument(e) {
    const digits = digitsOf(e.target.value).slice(0, 14);
    setForm((prev) => ({ ...prev, document: applyMask(digits, getCpfCnpjMask(digits), digitsOf(prev.document)) }));
  }

  function validate() {
    if (!coreLocked) {
      if (form.legalName.trim().length < 2) return "Informe o nome ou razão social.";
      const document = digitsOf(form.document);
      const documentValid = document.length === 14 ? isValidCnpj(document) : isValidCpf(document);
      if ((!isEdit || document.length > 0) && !documentValid) return "CPF ou CNPJ inválido. Confira os números digitados.";
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
    const payload = {
      legalName: form.legalName.trim(),
      document: digitsOf(form.document) || null,
      tradeName: form.tradeName.trim() || null,
      category: form.category.trim() || null,
    };

    if (isEdit) {
      try {
        onSaved(await updateSupplier(supplier.id, payload));
      } catch (err) {
        setError(err.message || "Não foi possível salvar o fornecedor.");
        setSubmitting(false);
      }
      return;
    }

    let saved = created;
    if (!saved) {
      try {
        saved = await createSupplier(payload);
      } catch (err) {
        setError(err.message || "Não foi possível cadastrar o fornecedor.");
        setSubmitting(false);
        return;
      }
    }

    try {
      const withPayment = await savePaymentChoice(payment, { pix, bank }, (payload) => addSupplierPaymentMethod(saved.id, payload));
      onSaved(withPayment ?? saved);
    } catch (err) {
      setCreated(saved);
      setError(paymentFailureMessage("Fornecedor", payment, err));
      setSubmitting(false);
    }
  }

  const close = created ? () => onSaved(created) : onClose;
  const submitLabel = isEdit ? "Salvar" : created ? (payment === "later" ? "Concluir" : "Salvar pagamento") : "Cadastrar";

  return (
    <FormDialog
      open
      title={isEdit ? "Editar dados cadastrais" : "Cadastrar fornecedor"}
      error={error}
      submitLabel={submitLabel}
      cancelLabel={created ? "Configurar depois" : "Cancelar"}
      submitting={submitting}
      onSubmit={handleSubmit}
      onClose={close}
    >
      <fieldset className="form-dialog__section" disabled={coreLocked}>
        {!isEdit && <legend className="form-dialog__section-title">Dados do fornecedor</legend>}
        <div className="form-dialog__grid">
          <TextField
            label="Nome ou razão social"
            example="legalName"
            full
            value={form.legalName}
            onChange={set("legalName")}
            maxLength={150}
            required
          />
          <TextField
            label="CPF ou CNPJ"
            example={isEdit ? supplier.documentMasked : "cpfOrCnpj"}
            full
            help={isEdit ? "Em branco mantém o atual." : undefined}
            helpId="supplier-document-keep"
            inputMode="numeric"
            value={form.document}
            onChange={handleDocument}
            maxLength={CNPJ_MASK.length}
            required={!isEdit}
          />
          <TextField
            label="Nome fantasia (opcional)"
            example="tradeName"
            value={form.tradeName}
            onChange={set("tradeName")}
            maxLength={150}
          />
          <TextField
            label="Categoria (opcional)"
            example="supplierCategory"
            value={form.category}
            onChange={set("category")}
            maxLength={60}
            list="supplier-categories"
          />
          <datalist id="supplier-categories">
            {categories.map((c) => (
              <option key={c} value={c} />
            ))}
          </datalist>
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
          idPrefix="new-supplier-pix"
        />
      )}
    </FormDialog>
  );
}
