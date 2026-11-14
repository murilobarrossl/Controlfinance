import { useEffect, useMemo, useRef, useState } from "react";
import { getTransactionsReport } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import BankLogo from "../../../components/ui/BankLogo/BankLogo.jsx";
import RangePicker from "../../../components/ui/RangePicker/RangePicker.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import { ChevronDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import { formatMonthLong } from "../../../utils/monthLabel.js";
import { getMonthsWindow } from "../../../utils/monthlyTrend.js";

const PAGE_SIZE = 25;
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

function formatDayTitle(value) {
  const date = new Date(value);
  const weekday = date.toLocaleDateString("pt-BR", { weekday: "short", timeZone: "UTC" }).replace(".", "");
  return `${weekday} · ${formatDate(value)}`;
}

function StatementPager({ page, totalPages, totalCount, loading, onPage }) {
  const first = (page - 1) * PAGE_SIZE + 1;
  const last = Math.min(page * PAGE_SIZE, totalCount);
  const noun = totalCount === 1 ? "lançamento" : "lançamentos";

  return (
    <nav className="cartoes__pager" aria-label="Páginas do extrato">
      <span className="cartoes__pager-count">
        {totalPages > 1 ? `${first}–${last} de ${totalCount} ${noun}` : `${totalCount} ${noun}`}
      </span>
      {totalPages > 1 && (
        <div className="cartoes__pager-controls">
          <button
            type="button"
            className="cartoes__pager-button cartoes__pager-button--prev"
            onClick={() => onPage(page - 1)}
            disabled={loading || page <= 1}
            aria-label="Página anterior"
          >
            <ChevronDownIcon />
          </button>
          <span className="cartoes__pager-status" aria-live="polite">
            Página {page} de {totalPages}
          </span>
          <button
            type="button"
            className="cartoes__pager-button cartoes__pager-button--next"
            onClick={() => onPage(page + 1)}
            disabled={loading || page >= totalPages}
            aria-label="Próxima página"
          >
            <ChevronDownIcon />
          </button>
        </div>
      )}
    </nav>
  );
}

export default function CardStatement({ card, getBrand }) {
  const [monthOffset, setMonthOffset] = useState(0);
  const [page, setPage] = useState(1);
  const [result, setResult] = useState(null);
  // Contagem e totais são do mês inteiro, não da página: guardados por filtro para que trocar de
  // página não esconda o rodapé nem a barra de totais enquanto a página nova carrega.
  const [filterSummary, setFilterSummary] = useState(null);
  const sectionRef = useRef(null);
  const headingRef = useRef(null);

  const month = useMemo(() => getMonthsWindow(1, monthOffset)[0], [monthOffset]);
  const year = month.getUTCFullYear();
  const monthNumber = month.getUTCMonth() + 1;
  const filterKey = `${card.id}|${year}-${monthNumber}`;
  const requestKey = `${filterKey}|${page}`;
  const loading = result?.key !== requestKey;

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;

    getTransactionsReport(
      { bankAccountId: card.id, year, month: monthNumber, sortBy: "dueDate", sortDir: "desc", page, pageSize: PAGE_SIZE },
      { signal },
    )
      .then((report) => {
        if (signal.aborted) return;
        const pages = Math.max(1, Math.ceil(report.totalCount / PAGE_SIZE));
        // Uma sincronização entre duas páginas pode encurtar o mês: volta para a última que existe.
        if (page > pages) {
          setPage(pages);
          return;
        }
        setResult({ key: requestKey, report, error: "" });
        setFilterSummary({
          key: filterKey,
          totalCount: report.totalCount,
          totalIncome: report.totalIncome,
          totalExpense: report.totalExpense,
        });
      })
      .catch((err) => {
        if (signal.aborted || isAbortError(err)) return;
        setResult({ key: requestKey, report: null, error: err.message || "Não foi possível carregar o extrato." });
      });

    return () => controller.abort();
  }, [card.id, year, monthNumber, page, requestKey, filterKey]);

  const report = result?.key === requestKey ? result.report : null;
  const error = result?.key === requestKey ? result.error : "";
  const days = useMemo(() => groupByDay(report?.items ?? []), [report]);
  const summary = filterSummary?.key === filterKey ? filterSummary : null;
  const totalPages = summary ? Math.max(1, Math.ceil(summary.totalCount / PAGE_SIZE)) : 1;

  function goToPage(next) {
    setPage(next);
    const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches;
    sectionRef.current?.scrollIntoView({ block: "start", behavior: reduceMotion ? "auto" : "smooth" });
    headingRef.current?.focus({ preventScroll: true });
  }

  const brand = getBrand(card);

  return (
    <section ref={sectionRef} className="cartoes__section cartoes__section--statement" aria-labelledby="cartoes-extrato">
      <header className="cartoes__section-head">
        <div className="cartoes__statement-heading">
          <h2 id="cartoes-extrato" ref={headingRef} tabIndex={-1} className="cartoes__section-title">
            Extrato do cartão
          </h2>
          <p className="cartoes__statement-current">
            <BankLogo name={brand.name} logoUrl={brand.logoUrl} color={brand.color} size={20} />
            <span>
              <strong>{brand.name}</strong> · {card.name}
            </span>
          </p>
        </div>
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
      </header>

      {error ? (
        <p className="cartoes__error" role="alert">{error}</p>
      ) : (
        <>
          <dl className="cartoes__statement-totals">
            <div>
              <dt>Compras no mês</dt>
              <dd className="cartoes__statement-total--expense">
                {summary ? formatCurrency(summary.totalExpense) : <Skeleton width="6rem" height="1.2rem" />}
              </dd>
            </div>
            <div>
              <dt>Pagamentos e estornos</dt>
              <dd className="cartoes__statement-total--income">
                {summary ? formatCurrency(summary.totalIncome) : <Skeleton width="6rem" height="1.2rem" />}
              </dd>
            </div>
            <div>
              <dt>Lançamentos</dt>
              <dd>{summary ? summary.totalCount : <Skeleton width="3rem" height="1.2rem" />}</dd>
            </div>
          </dl>

          {loading ? (
            <Skeleton width="100%" height="8rem" />
          ) : days.length === 0 ? (
            <p className="cartoes__hint">Nenhum lançamento deste cartão em {formatMonthLong(month)}.</p>
          ) : (
            <div className="cartoes__statement-days">
              {days.map((day) => (
                <section key={day.key} className="cartoes__statement-day" aria-label={formatDate(day.key)}>
                  <h3 className="cartoes__statement-day-title">{formatDayTitle(day.key)}</h3>
                  <ul className="cartoes__rows">
                    {day.items.map((t) => {
                      const isIncome = t.type === "Income";
                      return (
                        <li key={t.id} className="cartoes__row cartoes__row--statement">
                          <span className="cartoes__row-info">
                            <span className="cartoes__row-name" title={t.name}>
                              {t.name}
                            </span>
                            <span className="cartoes__row-meta">
                              {t.categoryName || "Sem categoria"}
                              {t.isTransfer && " · Transferência"}
                            </span>
                          </span>
                          <span className="cartoes__row-end">
                            {t.status !== "Paid" && (
                              <StatusPill status={t.status}>{STATUS_LABELS[t.status] || t.status}</StatusPill>
                            )}
                            <span className={`cartoes__row-amount${isIncome ? " cartoes__row-amount--income" : ""}`}>
                              {isIncome ? "+" : "\u2212"}
                              {formatCurrency(t.amount)}
                            </span>
                          </span>
                        </li>
                      );
                    })}
                  </ul>
                </section>
              ))}
            </div>
          )}

          {summary && summary.totalCount > 0 && (
            <StatementPager
              page={page}
              totalPages={totalPages}
              totalCount={summary.totalCount}
              loading={loading}
              onPage={goToPage}
            />
          )}
        </>
      )}
    </section>
  );
}
