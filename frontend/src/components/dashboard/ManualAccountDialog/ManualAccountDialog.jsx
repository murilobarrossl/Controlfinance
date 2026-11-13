import { useEffect, useState } from "react";
import FormDialog from "../../ui/FormDialog/FormDialog.jsx";
import CurrencyInput from "../../ui/CurrencyInput/CurrencyInput.jsx";
import { createBankAccount } from "../../../api/bankAccounts.js";

const EMPTY = { name: "", balance: "" };

// Conta cadastrada à mão: o caminho pra usar o painel sem Open Finance (banco não suportado,
// dinheiro em espécie, conta de terceiro). Não sincroniza nem atualiza saldo sozinha — o saldo é
// o que a pessoa digitou, e por isso a tela diz isso em vez de deixar parecer dado de banco.
export default function ManualAccountDialog({ open, onClose, onCreated }) {
  const [form, setForm] = useState(EMPTY);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (open) {
      setForm(EMPTY);
      setError("");
    }
  }, [open]);

  async function handleSubmit(e) {
    e.preventDefault();
    if (!form.name.trim()) return setError("Dê um nome à conta.");

    const balance = form.balance === "" ? 0 : Number(form.balance);
    if (!Number.isFinite(balance)) return setError("Saldo inválido.");

    setSubmitting(true);
    setError("");
    try {
      const account = await createBankAccount({ name: form.name.trim(), balance });
      onCreated?.(account);
      onClose();
    } catch (err) {
      setError(err.message || "Não foi possível cadastrar a conta.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <FormDialog
      open={open}
      title="Cadastrar conta manual"
      description="Para uma conta que você controla por fora do Open Finance. Ela não sincroniza com banco nenhum: o saldo é o que você informar aqui e só muda com os lançamentos que você cadastrar."
      submitLabel="Cadastrar conta"
      submitting={submitting}
      error={error}
      onSubmit={handleSubmit}
      onClose={onClose}
    >
      <label className="form-dialog__field">
        <span className="form-dialog__label">Nome da conta</span>
        <input
          type="text"
          value={form.name}
          onChange={(e) => setForm((prev) => ({ ...prev, name: e.target.value }))}
          maxLength={100}
          placeholder="Ex: Carteira, Conta da loja"
          required
        />
      </label>

      <label className="form-dialog__field">
        <span className="form-dialog__label">Saldo de hoje</span>
        <CurrencyInput
          value={form.balance}
          onChange={(e) => setForm((prev) => ({ ...prev, balance: e.target.value }))}
          min="-1000000000"
          placeholder="Ex: 500"
        />
        <span className="form-dialog__help">
          Quanto há nessa conta agora. Pode deixar em branco para começar do zero.
        </span>
      </label>
    </FormDialog>
  );
}
