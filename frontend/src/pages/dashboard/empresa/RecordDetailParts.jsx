import { useEffect, useId, useState } from "react";
import { isAbortError } from "../../../api/client.js";
import { payeeAccountTypeLabel, pixKeyTypeLabel } from "../../../utils/employees.js";
import { applyMask, CNPJ_MASK, CPF_MASK } from "../../../utils/masks.js";

export function Field({ label, children, muted = false, tone = "default" }) {
  return (
    <div className="record-detail__field">
      <dt>{label}</dt>
      <dd className={muted ? "record-detail__value--muted" : `record-detail__value--${tone}`}>{children}</dd>
    </div>
  );
}

export function Section({ id, title, action, children }) {
  return (
    <section className="record-detail__section" aria-labelledby={id}>
      <div className="record-detail__section-head">
        <h2 id={id}>{title}</h2>
        {action && <div className="record-detail__section-actions">{action}</div>}
      </div>
      {children}
    </section>
  );
}

export function TextButton({ children, tone, ...props }) {
  return (
    <button type="button" className={`record-detail__text-button${tone ? ` record-detail__text-button--${tone}` : ""}`} {...props}>
      {children}
    </button>
  );
}

export function AddButton({ children, ...props }) {
  return (
    <button type="button" className="record-detail__add-button" {...props}>
      <svg viewBox="0 0 16 16" width="12" height="12" aria-hidden="true">
        <path d="M8 3v10M3 8h10" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
      </svg>
      {children}
    </button>
  );
}

function formatPhone(key) {
  const digits = key.replace(/\D/g, "");
  const local = digits.startsWith("55") && digits.length >= 12 ? digits.slice(2) : digits;
  if (local.length !== 10 && local.length !== 11) return key;
  return `+55 (${local.slice(0, 2)}) ${local.slice(2, -4)}-${local.slice(-4)}`;
}

function formatPixKey(type, key) {
  if (type === "Cpf") return applyMask(key, CPF_MASK);
  if (type === "Cnpj") return applyMask(key, CNPJ_MASK);
  if (type === "Phone") return formatPhone(key);
  return key;
}

// Titular de quem recebe (declarado). Método antigo sem titular: avisa, porque sem ele o pagamento
// por chave e-mail/celular/aleatória só é reconhecido pelo CPF do próprio cadastro.
function HolderLine({ method }) {
  if (method.holderDocumentMasked) return <span className="record-detail__method-label">Titular {method.holderDocumentMasked}</span>;
  if (method.holderMissing) return <span className="record-detail__method-label">Titular não informado</span>;
  return null;
}

function PixMethodItem({ method, pix, value, error, onEdit, onRemove }) {
  return (
    <li className="record-detail__method">
      <div>
        <span className="record-detail__method-label">Pix · {pixKeyTypeLabel(pix.type)}</span>
        <span className="record-detail__method-value">{value}</span>
        {error && <span className="record-detail__value--muted">{error}</span>}
        <HolderLine method={method} />
      </div>
      <div className="record-detail__method-actions">
        <TextButton onClick={onEdit}>Alterar</TextButton>
        <TextButton tone="danger" onClick={onRemove}>
          Remover
        </TextButton>
      </div>
    </li>
  );
}

function BankMethodItem({ method, bank, onEdit, onRemove }) {
  return (
    <li className="record-detail__method">
      <div>
        <span className="record-detail__method-label">Transferência (TED) · {payeeAccountTypeLabel(bank.accountType)}</span>
        <span className="record-detail__method-value">
          Banco {bank.bankCode} · Agência {bank.branch} · Conta {bank.accountMasked}
        </span>
        <HolderLine method={method} />
      </div>
      <div className="record-detail__method-actions">
        <TextButton onClick={onEdit}>Alterar</TextButton>
        <TextButton tone="danger" onClick={onRemove}>
          Remover
        </TextButton>
      </div>
    </li>
  );
}

// Lista de métodos (vários Pix e contas). As chaves Pix aparecem completas: são carregadas pela rota
// própria a cada alteração do cadastro (updatedAt), nunca ficam desatualizadas depois de "Alterar".
export function PaymentMethodListSection({ id, payee, loadPixKey, onAdd, onEdit, onRemove }) {
  const methods = payee.paymentMethods ?? [];
  const pixIds = methods.filter((m) => m.kind === "Pix").map((m) => m.id);
  const version = pixIds.length ? `${payee.id}|${payee.updatedAt}|${pixIds.join(",")}` : null;
  const [full, setFull] = useState(null);

  useEffect(() => {
    if (!version) return undefined;
    let active = true;
    const ids = version.split("|")[2].split(",");
    Promise.all(
      ids.map((methodId) =>
        loadPixKey(payee.id, methodId)
          .then((res) => [methodId, { key: formatPixKey(res.type, res.key) }])
          .catch((err) => [methodId, { error: err.message || "Não foi possível carregar a chave Pix." }]),
      ),
    ).then((entries) => active && setFull({ version, keys: Object.fromEntries(entries) }));
    return () => {
      active = false;
    };
  }, [version, loadPixKey, payee.id]);

  const keys = full?.version === version ? full.keys : null;

  return (
    <Section id={id} title="Dados de pagamento" action={<AddButton onClick={onAdd}>Adicionar método de pagamento</AddButton>}>
      {methods.length === 0 ? (
        <p className="record-detail__empty">Nenhum dado de pagamento.</p>
      ) : (
        <ul className="record-detail__methods">
          {methods.map((m) =>
            m.kind === "Pix" ? (
              <PixMethodItem
                key={m.id}
                method={m}
                pix={m.pix}
                value={keys ? keys[m.id]?.key ?? "" : "Carregando…"}
                error={keys?.[m.id]?.error}
                onEdit={() => onEdit(m)}
                onRemove={() => onRemove(m)}
              />
            ) : (
              <BankMethodItem key={m.id} method={m} bank={m.bank} onEdit={() => onEdit(m)} onRemove={() => onRemove(m)} />
            ),
          )}
        </ul>
      )}
    </Section>
  );
}

export function monthLabel(key) {
  const [year, month] = (key ?? "").split("-").map(Number);
  if (!year || !month) return "";
  return new Intl.DateTimeFormat("pt-BR", { month: "long", year: "numeric" }).format(new Date(year, month - 1, 1));
}

// Pagamento do mês: o servidor decide o mês (fuso do usuário) e devolve o estado final e o valor
// identificado pelo banco (o único valor pago exibido). A marcação manual é só status. A gravação
// envia o mês exibido; se o mês virou, o servidor recusa e o estado é recarregado.
export function usePaymentMonth(payeeId, load, save) {
  const [result, setResult] = useState(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const state = result?.key === payeeId ? result.data : null;

  useEffect(() => {
    const controller = new AbortController();
    load(payeeId, { signal: controller.signal })
      .then((data) => setResult({ key: payeeId, data }))
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setResult({ key: payeeId, data: null });
        setError(err.message || "Não foi possível carregar o pagamento do mês.");
      });
    return () => controller.abort();
  }, [payeeId, load]);

  async function toggle(paid) {
    if (!state || saving) return;
    setSaving(true);
    setError("");
    try {
      setResult({ key: payeeId, data: await save(payeeId, { referenceMonth: state.referenceMonth, paid }) });
    } catch (err) {
      setError(err.message || "Não foi possível salvar.");
      if (err.status === 409) load(payeeId).then((data) => setResult({ key: payeeId, data })).catch(() => {});
    } finally {
      setSaving(false);
    }
  }

  return { state, loaded: Boolean(result), saving, error, toggle };
}

// Origem do status, só quando acrescenta algo: marcação manual nunca aparece como confirmação do banco.
function paidNote({ source, bankIdentified }) {
  if (source === "Bank") return "Identificado automaticamente pelo banco.";
  if (source === "ManualPaid")
    return bankIdentified ? "Marcado por você. O banco também identificou um pagamento." : "Marcado por você, sem confirmação do banco.";
  if (source === "ManualUnpaid" && bankIdentified) return "Desmarcado por você. O banco identificou um pagamento neste mês.";
  return null;
}

function CloseIcon() {
  return (
    <svg viewBox="0 0 16 16" width="12" height="12" aria-hidden="true">
      <path d="M4 4l8 8M12 4l-8 8" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

// Controle secundário, no fim da página: a identificação automática é o caminho principal. O aviso
// explicativo some ao fechar só enquanto a página está aberta (estado local, nada é salvo).
export function PaymentMonthToggle({ month }) {
  const { state, loaded, saving, error, toggle } = month;
  const [noticeOpen, setNoticeOpen] = useState(true);
  const id = useId();
  const note = error || (state ? paidNote(state) : loaded ? null : "Carregando…");
  return (
    <div className="record-detail__paid" aria-busy={!loaded || saving}>
      <label className="record-detail__paid-check">
        <input
          type="checkbox"
          checked={Boolean(state?.paid)}
          disabled={!state || saving}
          onChange={(e) => toggle(e.target.checked)}
          aria-describedby={note ? id : undefined}
        />
        <span>Pago este mês</span>
        {state && <span className="record-detail__paid-month">{monthLabel(state.referenceMonth)}</span>}
      </label>
      {note && (
        <p id={id} className={error ? "record-detail__paid-note record-detail__paid-note--error" : "record-detail__paid-note"}>
          {note}
        </p>
      )}
      {noticeOpen && (
        <div className="record-detail__paid-notice">
          <p>
            O Control Finance verifica automaticamente os pagamentos realizados pelas contas bancárias conectadas. Quando uma
            transferência ao funcionário é identificada com segurança, o pagamento é reconhecido automaticamente.
          </p>
          <button type="button" className="record-detail__notice-close" onClick={() => setNoticeOpen(false)} aria-label="Fechar aviso">
            <CloseIcon />
          </button>
        </div>
      )}
    </div>
  );
}
