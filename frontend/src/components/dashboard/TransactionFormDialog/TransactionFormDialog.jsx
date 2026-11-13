import { useEffect, useState } from "react";
import FormDialog from "../../ui/FormDialog/FormDialog.jsx";
import CurrencyInput from "../../ui/CurrencyInput/CurrencyInput.jsx";
import { createTransaction } from "../../../api/transactions.js";
import { getCategories } from "../../../api/categories.js";
import { isAbortError } from "../../../api/client.js";
import { isCreditCardAccount } from "../../../utils/accountKind.js";

function todayIso() {
  const now = new Date();
  const pad = (n) => String(n).padStart(2, "0");
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

const emptyForm = (accountId) => ({
  type: "Expense",
  name: "",
  amount: "",
  dueDate: todayIso(),
  status: "Paid",
  bankAccountId: accountId ?? "",
  categoryId: "",
  isFixed: false,
});

// Cadastro de receita/despesa à mão: o que a landing promete pra quem não conecta banco, e o
// complemento pra quem conecta (dinheiro em espécie, conta de outro banco). Lançamento manual não
// é apagado nem sobrescrito pela sincronização: o upsert da Polp casa por id dela (Description).
export default function TransactionFormDialog({ open, accounts, defaultAccountId, onClose, onCreated }) {
  const [form, setForm] = useState(() => emptyForm(defaultAccountId));
  const [categories, setCategories] = useState([]);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  const set = (field) => (e) => setForm((prev) => ({ ...prev, [field]: e.target.value }));

  // Reabrir o formulário sempre começa limpo, na conta que a tela está mostrando: reaproveitar o
  // que foi digitado antes faria o usuário lançar de novo, por engano, o que acabou de lançar.
  useEffect(() => {
    if (open) {
      setForm(emptyForm(defaultAccountId));
      setError("");
    }
  }, [open, defaultAccountId]);

  useEffect(() => {
    if (!open) return undefined;
    const controller = new AbortController();
    getCategories({ signal: controller.signal })
      .then((data) => setCategories(Array.isArray(data) ? data : []))
      .catch((err) => {
        // Sem categorias o lançamento ainda é válido ("Sem categoria"), então a falha aqui não
        // bloqueia o formulário nem vira erro na tela.
        if (!isAbortError(err)) setCategories([]);
      });
    return () => controller.abort();
  }, [open]);

  async function handleSubmit(e) {
    e.preventDefault();
    const amount = Number(form.amount);
    if (!form.name.trim()) return setError("Dê um nome ao lançamento.");
    if (!(amount > 0)) return setError("O valor precisa ser maior que zero.");
    if (!form.dueDate) return setError("Informe a data.");

    setSubmitting(true);
    setError("");
    try {
      await createTransaction({
        name: form.name.trim(),
        type: form.type,
        status: form.status,
        amount,
        dueDate: form.dueDate,
        bankAccountId: form.bankAccountId,
        categoryId: form.categoryId,
        isFixed: form.isFixed,
      });
      onCreated?.();
      onClose();
    } catch (err) {
      setError(err.message || "Não foi possível salvar o lançamento.");
    } finally {
      setSubmitting(false);
    }
  }

  const isIncome = form.type === "Income";
  // Cartão de crédito não recebe lançamento manual aqui: o gasto no crédito entra pela fatura e
  // pelos parcelamentos, na aba Cartões, e somá-lo como saída da conta contaria duas vezes.
  const selectableAccounts = accounts.filter((a) => !isCreditCardAccount(a));

  return (
    <FormDialog
      open={open}
      title="Novo lançamento"
      description="Para dinheiro que não vem de um banco conectado: pagamento em espécie, conta de outro banco ou algo que você quer acompanhar à mão."
      submitLabel="Salvar lançamento"
      submitting={submitting}
      error={error}
      onSubmit={handleSubmit}
      onClose={onClose}
    >
      <div className="form-dialog__field">
        <span className="form-dialog__label" id="transaction-type-label">
          É entrada ou saída?
        </span>
        <div className="form-dialog__choice" role="group" aria-labelledby="transaction-type-label">
          <button
            type="button"
            aria-pressed={isIncome}
            onClick={() => setForm((prev) => ({ ...prev, type: "Income" }))}
          >
            Entrada (recebi)
          </button>
          <button
            type="button"
            className="form-dialog__choice--expense"
            aria-pressed={!isIncome}
            onClick={() => setForm((prev) => ({ ...prev, type: "Expense" }))}
          >
            Saída (paguei)
          </button>
        </div>
      </div>

      <div className="form-dialog__grid">
        <label className="form-dialog__field form-dialog__field--full">
          <span className="form-dialog__label">Descrição</span>
          <input
            type="text"
            value={form.name}
            onChange={set("name")}
            maxLength={150}
            placeholder={isIncome ? "Ex: Freela de novembro" : "Ex: Feira da semana"}
            required
          />
        </label>

        <label className="form-dialog__field">
          <span className="form-dialog__label">Valor</span>
          <CurrencyInput value={form.amount} onChange={set("amount")} placeholder="Ex: 150" required />
        </label>

        <label className="form-dialog__field">
          <span className="form-dialog__label">Data</span>
          <input type="date" value={form.dueDate} onChange={set("dueDate")} required />
        </label>

        <label className="form-dialog__field">
          <span className="form-dialog__label">Situação</span>
          <select value={form.status} onChange={set("status")}>
            <option value="Paid">{isIncome ? "Já recebi" : "Já paguei"}</option>
            <option value="Pending">{isIncome ? "Ainda vou receber" : "Ainda vou pagar"}</option>
          </select>
        </label>

        <label className="form-dialog__field">
          <span className="form-dialog__label">Categoria</span>
          <select value={form.categoryId} onChange={set("categoryId")}>
            <option value="">Sem categoria</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </label>

        {selectableAccounts.length > 0 && (
          <label className="form-dialog__field form-dialog__field--full">
            <span className="form-dialog__label">Conta</span>
            <select value={form.bankAccountId} onChange={set("bankAccountId")}>
              <option value="">Sem conta vinculada</option>
              {selectableAccounts.map((account) => (
                <option key={account.id} value={account.id}>
                  {account.name}
                </option>
              ))}
            </select>
            <span className="form-dialog__help">
              Escolha a conta de onde o dinheiro saiu ou entrou, para o lançamento aparecer junto com os extratos dela.
            </span>
          </label>
        )}
      </div>

      <label className="form-dialog__checkbox">
        <input type="checkbox" checked={form.isFixed} onChange={(e) => setForm((prev) => ({ ...prev, isFixed: e.target.checked }))} />
        <span>Esse valor se repete todo mês (aluguel, salário, assinatura).</span>
      </label>
    </FormDialog>
  );
}
