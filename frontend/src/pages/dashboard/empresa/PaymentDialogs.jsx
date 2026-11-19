import { useState } from "react";
import FormDialog from "../../../components/ui/FormDialog/FormDialog.jsx";
import { BankFields, PaymentSegmented, PixFields } from "./PaymentFields.jsx";
import { EMPTY_BANK, EMPTY_PIX, PAYMENT_METHOD_OPTIONS, paymentMethodPayload, paymentProblem } from "./payeePayment.js";
import useDialogSubmit from "./useDialogSubmit.js";

function validatedSubmit(problem, setError, submit) {
  return (e) => {
    if (problem) {
      e.preventDefault();
      setError(problem);
      return;
    }
    submit(e);
  };
}

// Incluir ou alterar um método (Pix ou conta para TED). Alterar pode trocar o tipo; a chave Pix e o
// número da conta atuais nunca vêm preenchidos, só o tipo de chave, banco, agência e tipo de conta.
export function PaymentMethodDialog({ current, save, onClose, onSaved }) {
  const [payment, setPayment] = useState(current?.kind === "Bank" ? "bank" : "pix");
  const [pix, setPix] = useState({ ...EMPTY_PIX, type: current?.pix?.type ?? EMPTY_PIX.type });
  const [bank, setBank] = useState({
    ...EMPTY_BANK,
    bankCode: current?.bank?.bankCode ?? "",
    branch: current?.bank?.branch ?? "",
    accountType: current?.bank?.accountType ?? EMPTY_BANK.accountType,
  });
  const { submitting, error, setError, submit } = useDialogSubmit(() => save(paymentMethodPayload(payment, { pix, bank })), onSaved);

  return (
    <FormDialog
      open
      title={current ? "Alterar método de pagamento" : "Adicionar método de pagamento"}
      error={error}
      submitting={submitting}
      onSubmit={validatedSubmit(paymentProblem(payment, pix, bank), setError, submit)}
      onClose={onClose}
    >
      <PaymentSegmented
        options={PAYMENT_METHOD_OPTIONS}
        value={payment}
        onChange={(value) => {
          setPayment(value);
          setError("");
        }}
      />
      {payment === "pix" ? (
        <PixFields value={pix} onChange={setPix} idPrefix="payment-method-pix" holderPlaceholder={current?.holderDocumentMasked} />
      ) : (
        <BankFields
          value={bank}
          onChange={setBank}
          accountPlaceholder={current?.kind === "Bank" ? current.bank?.accountMasked : undefined}
          holderPlaceholder={current?.holderDocumentMasked}
        />
      )}
    </FormDialog>
  );
}
