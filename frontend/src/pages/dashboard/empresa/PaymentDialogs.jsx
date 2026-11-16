import { useState } from "react";
import FormDialog from "../../../components/ui/FormDialog/FormDialog.jsx";
import { BankFields, PixFields } from "./PaymentFields.jsx";
import { bankPayload, bankProblem, EMPTY_BANK, EMPTY_PIX, pixPayload, pixProblem } from "./payeePayment.js";
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

export function PixDialog({ current, save, onClose, onSaved }) {
  const [pix, setPix] = useState({ ...EMPTY_PIX, type: current?.type ?? EMPTY_PIX.type });
  const { submitting, error, setError, submit } = useDialogSubmit(() => save(pixPayload(pix)), onSaved);

  return (
    <FormDialog
      open
      title={current ? "Alterar chave Pix" : "Adicionar chave Pix"}
      error={error}
      submitting={submitting}
      onSubmit={validatedSubmit(pixProblem(pix), setError, submit)}
      onClose={onClose}
    >
      <PixFields value={pix} onChange={setPix} idPrefix="payee-pix" />
    </FormDialog>
  );
}

export function BankAccountDialog({ current, save, onClose, onSaved }) {
  const [bank, setBank] = useState({
    ...EMPTY_BANK,
    bankCode: current?.bankCode ?? "",
    branch: current?.branch ?? "",
    accountType: current?.accountType ?? EMPTY_BANK.accountType,
  });
  const { submitting, error, setError, submit } = useDialogSubmit(() => save(bankPayload(bank)), onSaved);

  return (
    <FormDialog
      open
      title={current ? "Alterar conta para transferência" : "Adicionar conta para transferência"}
      error={error}
      submitting={submitting}
      onSubmit={validatedSubmit(bankProblem(bank), setError, submit)}
      onClose={onClose}
    >
      <BankFields value={bank} onChange={setBank} accountPlaceholder={current ? current.accountMasked : undefined} />
    </FormDialog>
  );
}
