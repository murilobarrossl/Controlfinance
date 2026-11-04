import { useEffect, useMemo, useState } from "react";
import { getTransactionsReport } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import Card from "../../../components/ui/Card/Card.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import { getMonthsWindow } from "../../../utils/monthlyTrend.js";

const PAGE_SIZE = 30;
const STATUS_LABELS = { Pending: "Pendente", Paid: "Pago", Overdue: "Atrasado" };

// Agrupa por dia (a lista já vem ordenada por data, da mais recente pra mais antiga), pra o extrato
// ler como o do app do banco: um cabeçalho por data e as compras daquele dia embaixo.
function groupByDay(items) {
  const days = [];
  for (const item of items) {
    const key = String(item.dueDate).slice(0, 10);
    const last = days[days.length - 1];
    if (last && last.key === key) last.items.push(item);
    else days.push({ key, items: [item] });
  }
  return days;
}

// Extrato de um cartão reconhecido: o cartão sincronizado é uma conta (BankAccount), então o extrato
// é o mesmo relatório de transações filtrado por ela. Cartões cadastrados à mão não têm lançamentos
// (só limite e fatura digitados), por isso não entram aqui.
export default function CardStatement({ cards, getBrand }) {
  const syncedCards = useMemo(() => cards.filter((c) => c.isSynced), [cards]);
  const [selectedId, setSelectedId] = useState(null);
  const [monthOffset, setMonthOffset] = useState(0);
  const [page, setPage] = useState(1);
  const [result, setResult] = useState(null);

  // Se o cartão escolhido sumiu (ex.: banco desconectado), volta pro primeiro disponível.
  const activeCard = syncedCards.find((c) => c.id === selectedId) ?? syncedCards[0] ?? null;
  const month = useMemo(() => getMonthsWindow(1, monthOffset)[0], [monthOffset]);
  const year = month.getUTCFullYear();
  const monthNumber = month.getUTCMonth() + 1;
  const requestKey = activeCard ? `${activeCard.id}|${year}-${monthNumber}|${page}` : null;
  const loading = Boolean(activeCard) && result?.key !== requestKey;

  useEffect(() => {
    if (!activeCard) return undefined;

    const controller = new AbortController();
    const { signal } = controller;

    getTransactionsReport(
      { bankAccountId: activeCard.id, year, month: monthNumber, sortBy: "dueDate", sortDir: "desc", page, pageSize: PAGE_SIZE },
      { signal },
    )
      .then((report) => {
        if (!signal.aborted) setResult({ key: requestKey, report, error: "" });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key: requestKey, report: null, error: err.message || "Não foi possível carregar o extrato." });
      });

    return () => controller.abort();
  }, [activeCard, year, monthNumber, page, requestKey]);

  const report = result?.key === requestKey ? result.report : null;
  const error = result?.key === requestKey ? result.error : "";
  const days = useMemo(() => groupByDay(report?.items ?? []), [report]);
  const totalPages = report ? Math.max(1, Math.ceil(report.totalCount / PAGE_SIZE)) : 1;

  if (syncedCards.length === 0) {
    return (
      <Card title="Extrato do cartão">
        <p className="cartoes__hint">
          O extrato aparece aqui pros cartões que vêm de um banco conectado. Cartões cadastrados manualmente só têm
          limite e fatura digitados por você, sem lançamentos.
        </p>
      </Card>
    );
  }

  return (
    <Card title="Extrato do cartão">
      <p className="cartoes__hint cartoes__statement-intro">
        Compras e pagamentos do cartão escolhido, mês a mês, como chegaram do banco na última sincronização.
      </p>

      <div className="cartoes__statement-cards" role="tablist" aria-label="Escolha o cartão">
        {syncedCards.map((card) => {
          const brand = getBrand(card);
          const active = card.id === activeCard.id;
          return (
            <button
              key={card.id}
              type="button"
              role="tab"
              aria-selected={active}
              className={`cartoes__statement-card ${active ? "cartoes__statement-card--active" : ""}`}
              style={{ "--group-color": brand.color ?? "var(--color-border-strong)" }}
              onClick={() => {
                setSelectedId(card.id);
                setPage(1);
              }}
            >
              <BankLogo name={brand.name} logoUrl={brand.logoUrl} color={brand.color} size={28} />
              <span className="cartoes__statement-card-text">
                <span className="cartoes__statement-card-bank">{brand.name}</span>
                <span className="cartoes__statement-card-name">{card.name}</span>
              </span>
            </button>
          );
        })}
      </div>

      <div className="cartoes__statement-toolbar">
        <RangePicker
          label={formatMonthLong(month)}
          onPrev={() => {
            setMonthOffset((prev) => prev + 1);
            setPage(1);
          }}
          onNext={() => {
            setMonthOffset((prev) => Math.max(0, prev - 1));
            setPage(1);
          }}
          nextDisabled={monthOffset === 0}
        />
      </div>

      {error ? (
        <p className="cartoes__error">{error}</p>
      ) : (
        <>
          <div className="cartoes__statement-totals">
            <div>
              <span className="cartoes__statement-total-label">Compras no mês</span>
              <strong className="cartoes__statement-total-value cartoes__statement-total-value--expense">
                {loading ? <Skeleton width="6rem" height="1.2rem" /> : formatCurrency(report?.totalExpense ?? 0)}
              </strong>
            </div>
            <div>
              <span className="cartoes__statement-total-label">Pagamentos e estornos</span>
              <strong className="cartoes__statement-total-value cartoes__statement-total-value--income">
                {loading ? <Skeleton width="6rem" height="1.2rem" /> : formatCurrency(report?.totalIncome ?? 0)}
              </strong>
            </div>
            <div>
              <span className="cartoes__statement-total-label">Lançamentos</span>
              <strong className="cartoes__statement-total-value">
                {loading ? <Skeleton width="3rem" height="1.2rem" /> : (report?.totalCount ?? 0)}
              </strong>
            </div>
          </div>

          {loading ? (
            <Skeleton width="100%" height="8rem" />
          ) : days.length === 0 ? (
            <p className="cartoes__hint">Nenhum lançamento deste cartão em {formatMonthLong(month)}.</p>
          ) : (
            <div className="cartoes__statement-days">
              {days.map((day) => (
                <section key={day.key} className="cartoes__statement-day">
                  <h4 className="cartoes__statement-day-title">{formatDate(day.key)}</h4>
                  <ul className="cartoes__statement-list">
                    {day.items.map((t) => {
                      const isIncome = t.type === "Income";
                      return (
                        <li key={t.id} className="cartoes__statement-item">
                          <div className="cartoes__list-info">
                            <span className="cartoes__list-name" title={t.name}>
                              {t.name}
                            </span>
                            <span className="cartoes__list-meta">
                              {t.categoryName || "Sem categoria"}
                              {t.isTransfer && " · Transferência"}
                            </span>
                          </div>
                          {t.status !== "Paid" && (
                            <StatusPill status={t.status}>{STATUS_LABELS[t.status] || t.status}</StatusPill>
                          )}
                          <span
                            className={`cartoes__statement-amount ${isIncome ? "cartoes__statement-amount--income" : ""}`}
                          >
                            {isIncome ? "+ " : "− "}
                            {formatCurrency(t.amount)}
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                </section>
              ))}
            </div>
          )}

          {totalPages > 1 && (
            <div className="cartoes__statement-pager">
              <Button
                as="button"
                type="button"
                variant="outline"
                size="sm"
                disabled={page <= 1 || loading}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                Anterior
              </Button>
              <span>
                Página {page} de {totalPages}
              </span>
              <Button
                as="button"
                type="button"
                variant="outline"
                size="sm"
                disabled={page >= totalPages || loading}
                onClick={() => setPage((p) => p + 1)}
              >
                Próxima
              </Button>
            </div>
          )}
        </>
      )}
    </Card>
  );
}
