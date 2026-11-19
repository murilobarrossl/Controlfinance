import { useEffect, useState } from "react";
import { closeCashDay, getAdjustedCashDays, getCashDay, reopenCashDay } from "../../../api/cashClosings.js";
import { isAbortError } from "../../../api/client.js";
import Button from "../../../components/ui/Button/Button.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import { formatDateTime, formatShortDay, formatSignedMoney } from "./cashFormat.js";

const KIND_LABEL = { added: "Nova", changed: "Corrigida", removed: "Removida" };

const cents = (value) => Math.round(value * 100) / 100;

function typedAmount(type, amount) {
  if (typeof amount !== "number") return "—";
  return `${type === "Income" ? "+" : "−"}${formatCurrency(Math.abs(amount))}`;
}

function balance(value) {
  return typeof value === "number" ? formatCurrency(value) : "Indisponível";
}

function Figure({ label, value, tone = "default" }) {
  return (
    <div className="cash-close__figure">
      <dt>{label}</dt>
      <dd className={`cash-close__figure-value cash-close__figure-value--${tone}`}>{value}</dd>
    </div>
  );
}

function AdjustmentEffect({ adjustment }) {
  const parts = [];
  if (adjustment.incomeDelta !== 0) parts.push(`Receitas ${formatSignedMoney(adjustment.incomeDelta)}`);
  if (adjustment.expenseDelta !== 0) parts.push(`Despesas ${formatSignedMoney(adjustment.expenseDelta)}`);
  return <span className="cash-adj__effect">{parts.length ? parts.join(" · ") : "Fora dos totais"}</span>;
}

function Adjustments({ items }) {
  const income = cents(items.reduce((sum, a) => sum + a.incomeDelta, 0));
  const expense = cents(items.reduce((sum, a) => sum + a.expenseDelta, 0));
  return (
    <details className="cash-adj">
      <summary className="cash-close__note cash-close__note--warning">
        Recebido após o fechamento: receitas {formatSignedMoney(income)} · despesas {formatSignedMoney(expense)}
      </summary>
      <ul className="cash-adj__list">
        {items.map((a) => (
          <li key={`${a.kind}-${a.transactionId}`} className="cash-adj__item">
            <span className="cash-adj__kind">{KIND_LABEL[a.kind]}</span>
            <span className="cash-adj__desc">
              <span className="cash-adj__name">{a.name ?? "Movimentação removida"}</span>
              <span className="cash-adj__meta">
                {a.kind === "changed" && (
                  <>
                    {typedAmount(a.originalType, a.originalAmount)} → {typedAmount(a.currentType, a.currentAmount)}
                  </>
                )}
                {a.kind === "added" && <>Importada em {formatDateTime(a.importedAt)}</>}
                {a.kind === "removed" && <>No fechamento: {typedAmount(a.originalType, a.originalAmount)}</>}
              </span>
            </span>
            <AdjustmentEffect adjustment={a} />
          </li>
        ))}
      </ul>
    </details>
  );
}

// Só aparece quando a variação dos saldos lidos pelo banco não bate com o que foi importado:
// os saldos ficam como o banco informou e a sobra nunca é atribuída a uma movimentação.
function BalanceGap({ data }) {
  if (typeof data.balanceChange !== "number" || cents(data.unexplained) === 0) return null;
  const moved = cents(data.countedNet + data.excludedNet);
  return (
    <details className="cash-gap">
      <summary className="cash-close__note cash-close__note--warning">
        Saldos com diferença de {formatCurrency(Math.abs(cents(data.unexplained)))} em relação às movimentações importadas
      </summary>
      <dl className="cash-gap__rows">
        <div>
          <dt>Variação do saldo no banco</dt>
          <dd>{formatSignedMoney(cents(data.balanceChange))}</dd>
        </div>
        <div>
          <dt>Movimentações importadas nas contas</dt>
          <dd>{formatSignedMoney(moved)}</dd>
        </div>
      </dl>
      {data.readings.length > 0 && (
        <ul className="cash-gap__readings">
          {data.readings.map((r) => (
            <li key={r.accountName}>
              {r.accountName}: saldos lidos em {r.openingReadAt ? formatDateTime(r.openingReadAt) : "—"} e{" "}
              {r.closingReadAt ? formatDateTime(r.closingReadAt) : "—"}
            </li>
          ))}
        </ul>
      )}
    </details>
  );
}

export default function CashClosingPanel({ date, scopeQuery, scopeKey, refreshKey, onSelectDay }) {
  const [version, setVersion] = useState(0);
  const [result, setResult] = useState(null);
  const [adjustedDays, setAdjustedDays] = useState(null);
  const [busy, setBusy] = useState(false);
  const [actionFailure, setActionFailure] = useState(null);
  const [confirmReopen, setConfirmReopen] = useState(false);

  const key = `${scopeKey}|${date}|${refreshKey}|${version}`;
  const listKey = `${scopeKey}|${refreshKey}|${version}`;

  useEffect(() => {
    const controller = new AbortController();
    getCashDay({ date, ...scopeQuery }, { signal: controller.signal })
      .then((data) => setResult({ key, data, error: "" }))
      .catch((err) => {
        if (isAbortError(err) || controller.signal.aborted) return;
        setResult({ key, data: null, error: err.message || "Não foi possível carregar o caixa do dia." });
      });
    return () => controller.abort();
  }, [key, date, scopeQuery]);

  useEffect(() => {
    const controller = new AbortController();
    getAdjustedCashDays(scopeQuery, { signal: controller.signal })
      .then((days) => setAdjustedDays({ key: listKey, days }))
      .catch((err) => {
        if (isAbortError(err) || controller.signal.aborted) return;
        setAdjustedDays({ key: listKey, days: [], failed: true });
      });
    return () => controller.abort();
  }, [listKey, scopeQuery]);

  const dayKey = `${scopeKey}|${date}`;
  const actionError = actionFailure?.key === dayKey ? actionFailure.text : "";
  const current = result?.key === key ? result : null;
  const data = current?.data;
  const closing = data?.closing;
  const otherAdjusted = adjustedDays?.key === listKey ? adjustedDays.days.filter((d) => d.day !== date) : [];
  const adjustedFailed = adjustedDays?.key === listKey && adjustedDays.failed;

  async function run(action) {
    setBusy(true);
    setActionFailure(null);
    try {
      await action();
      setVersion((v) => v + 1);
    } catch (err) {
      setActionFailure({ key: dayKey, text: err.message || "Não foi possível concluir a ação." });
      if (err.status === 409) setVersion((v) => v + 1);
    } finally {
      setBusy(false);
    }
  }

  const handleClose = () =>
    run(() =>
      closeCashDay({
        date,
        bankAccountId: scopeQuery.bankAccountId,
        expectedIncome: data.current.income,
        expectedExpense: data.current.expense,
        expectedCount: data.current.count,
      })
    );

  const handleReopen = () => {
    setConfirmReopen(false);
    run(() => reopenCashDay(closing.id, closing.version));
  };

  if (current?.error) return <p className="receitas-despesas__error">{current.error}</p>;

  const figures = closing ?? data?.current;
  const loading = !data;

  return (
    <section className={`cash-close${closing ? " cash-close--closed" : ""}`} aria-labelledby="cash-close-title">
      <div className="cash-close__bar">
        <p id="cash-close-title" className="cash-close__status">
          {loading ? (
            <Skeleton width="10rem" height="1rem" />
          ) : closing ? (
            <>
              <span className="cash-close__state">Caixa fechado</span>
              <span className="cash-close__meta">
                {formatDateTime(closing.closedAt)}
                {closing.closedBy ? ` · ${closing.closedBy}` : ""}
              </span>
            </>
          ) : (
            <span className="cash-close__state">Caixa aberto</span>
          )}
        </p>
        {data?.reopenable && (
          <Button variant="outline" size="sm" onClick={() => setConfirmReopen(true)} disabled={busy}>
            {busy ? "Reabrindo…" : "Reabrir caixa"}
          </Button>
        )}
        {data?.closable && (
          <Button size="sm" onClick={handleClose} disabled={busy}>
            {busy ? "Fechando…" : "Fechar caixa"}
          </Button>
        )}
      </div>

      <dl className="cash-close__figures">
        {loading ? (
          Array.from({ length: 4 }, (_, i) => (
            <div key={i} className="cash-close__figure">
              <Skeleton width="5rem" height="0.8rem" />
              <Skeleton width="8rem" height="1.6rem" />
            </div>
          ))
        ) : (
          <>
            <Figure label="Saldo inicial" value={balance(figures.openingBalance)} />
            <Figure label="Receitas" value={formatCurrency(figures.income)} tone="income" />
            <Figure label="Despesas" value={formatCurrency(figures.expense)} tone="expense" />
            <Figure label="Saldo final" value={balance(figures.closingBalance)} tone="strong" />
          </>
        )}
      </dl>

      {actionError && (
        <p className="receitas-despesas__error" role="alert">
          {actionError}
        </p>
      )}

      {data && (
        <div className="cash-close__notes">
          <BalanceGap data={data.reconciliation} />
          {closing && data.adjustments.length > 0 && <Adjustments items={data.adjustments} />}
          {adjustedFailed && (
            <p className="receitas-despesas__error">Não foi possível carregar os dias fechados com ajustes.</p>
          )}
          {otherAdjusted.length > 0 && (
            <div className="cash-close__adjusted">
              <span>Dias fechados com ajustes</span>
              <ul>
                {otherAdjusted.map((d) => (
                  <li key={d.day}>
                    <button type="button" onClick={() => onSelectDay(d.day)}>
                      {formatShortDay(d.day)}
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      <ConfirmDialog
        open={confirmReopen}
        title="Reabrir o caixa de hoje?"
        message="Você poderá fechar de novo com os dados bancários mais recentes."
        confirmLabel="Reabrir caixa"
        onConfirm={handleReopen}
        onClose={() => setConfirmReopen(false)}
      />
    </section>
  );
}
