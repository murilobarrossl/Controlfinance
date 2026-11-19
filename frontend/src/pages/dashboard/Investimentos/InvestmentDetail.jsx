import { financialTone } from "../../../utils/financialTone.js";
import { useEffect, useMemo, useRef } from "react";
import { Link, Navigate, useParams } from "react-router";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { ChevronDownIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { dateLabel, formatExact, formatMoney, formatContractNumber, goalOf, isReserve, moneyField, normalizeInvestment, STATE_TEXT, sumMoney, validMoney } from "../../../utils/importedInvestments.js";
import { useInvestmentResource } from "./useInvestmentResource.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import { useAuth } from "../../../context/AuthContext.jsx";
import usePlanning from "./usePlanning.js";
import usePageTitle from "../../../hooks/usePageTitle.js";
import ObservedInvestmentChart from "./ObservedInvestmentChart.jsx";
import { InstitutionLogo, InvestmentIcon } from "./InvestmentMarks.jsx";
import { Field, Section, TextButton } from "../empresa/RecordDetailParts.jsx";
import "../empresa/RecordDetail.css";
import "./Investimentos.css";

const LIST_PATH = "/dashboard/investimentos";
const MOVEMENT_NAMES = {
  APLICACAO: "Aplicação", RESGATE: "Resgate", COMPRA: "Compra", VENDA: "Venda", VENCIMENTO: "Vencimento",
  PAGAMENTO_JUROS: "Pagamento de juros", AMORTIZACAO: "Amortização", DIVIDENDOS: "Dividendos", JCP: "Juros sobre capital próprio",
  ALUGUEIS: "Aluguel de ativos", COME_COTAS: "Come-cotas", CANCELAMENTO: "Cancelamento", PREMIO: "Prêmio", MULTA: "Multa", MORA: "Juros de mora",
  TRANSFERENCIA_COTAS: "Transferência de cotas", TRANSFERENCIA_CUSTODIA: "Transferência de custódia", TRANSFERENCIA_TITULARIDADE: "Transferência de titularidade",
};
const PERIODICITY = { ANUAL: "Anual", MENSAL: "Mensal", DIARIO: "Diária", SEMESTRAL: "Semestral" };
const RATE_TYPE = { LINEAR: "Linear", EXPONENCIAL: "Exponencial" };
const DAY_COUNT = { DIAS_UTEIS: "Dias úteis", DIAS_CORRIDOS: "Dias corridos" };
const YES_NO = { SIM: "Sim", NAO: "Não" };
const FIXED_INCOME = ["bank-fixed-incomes", "credit-fixed-incomes", "treasure-titles"];
const COSTS = ["income_tax", "financial_transaction_tax", "transaction_exit_fee", "income_tax_provision", "financial_transaction_tax_provision"];
function Figure({ label, values, note }) {
  return <div className="inv-page__figure">
    <dt>{label}</dt>
    {values.length ? values.map((value, index) => <dd key={index} className="inv-page__figure-value">{value}</dd>)
      : <dd className="inv-page__figure-value inv-page__figure--muted">Não informado</dd>}
    {note && <dd className="inv-page__figure-note">{note}</dd>}
  </div>;
}
const text = (label, value) => value ? <Field key={label} label={label}>{value}</Field> : null;
const rate = (label, value) => text(label, value != null && formatContractNumber(value) !== "Não informado" ? formatContractNumber(value) : null);
const date = (label, value) => text(label, value && dateLabel(value) !== "Não informado" ? dateLabel(value) : null);
function scalar(label, document, field) {
  const value = document?.availability?.[field] === "available" ? formatExact(document.values?.[field]) : null;
  return text(label, value !== "Não informado" && value);
}
function money(label, document, field) {
  const current = moneyField(document, field);
  const prior = !current && moneyField(document, field, true);
  if (!current && !prior) return null;
  const tone = COSTS.includes(field) && current ? financialTone(-Number(current.amount)) : "default";
  const observed = document?.provenance?.[field]?.observed_at;
  return <Field key={label} label={label} tone={tone}>{formatMoney(current ?? prior)}
    {prior && <small className="inv-real__prior">Último informado{observed ? ` · ${dateLabel(observed)}` : ""}</small>}</Field>;
}
function More({ dataset, onMore }) {
  return <>{dataset.moreError && <p className="investimentos__error" role="alert">Não foi possível carregar a próxima página.</p>}
    {dataset.cursor && <TextButton onClick={onMore} disabled={dataset.loadingMore || dataset.refreshing}>{dataset.loadingMore || dataset.refreshing ? "Carregando…" : "Carregar mais"}</TextButton>}</>;
}
function LoadError({ message, retry }) {
  return <p className="record-detail__empty" role="alert">{message} <TextButton onClick={retry}>Tentar novamente</TextButton></p>;
}
function observationMoney(row, reserved, field) {
  if (!reserved) return moneyField(row.payload, field);
  const amounts = row.payload?.values?.available_amount;
  return row.payload?.availability?.available_amount === "available" && amounts?.length === 1 ? validMoney(amounts[0]) : null;
}
function historyPoints(rows, reserved, field) {
  const grouped = new Map();
  for (const row of rows) {
    const reference = reserved ? row.observedAt : row.referenceAt;
    if (!reference || Number.isNaN(new Date(reference).getTime()) || new Date(reference).getTime() > Date.now()) continue;
    const list = grouped.get(reference) ?? []; list.push(row); grouped.set(reference, list);
  }
  return [...grouped.entries()].sort(([a], [b]) => new Date(a) - new Date(b)).map(([reference, versions]) => {
    const timestamps = versions.map((row) => new Date(row.payload?.values?.updated_at).getTime());
    const stamped = timestamps.every((value) => Number.isFinite(value));
    const newest = stamped ? Math.max(...timestamps) : null;
    const candidates = stamped ? versions.filter((_, index) => timestamps[index] === newest) : versions;
    const monies = candidates.map((row) => observationMoney(row, reserved, field));
    const conflict = monies.some((m) => !m) || new Set(monies.filter(Boolean).map((m) => `${m.currency}:${m.amount}`)).size > 1;
    const original = candidates[0]?.payload?.values;
    return { key: reference, referenceAt: reference, period: dateLabel(reserved ? reference : original?.reference_date_time ?? original?.reference_date ?? reference),
      money: conflict ? null : monies[0], time: new Date(reference).getTime(), collected: reserved };
  });
}
function negate(amount) {
  return amount.startsWith("-") ? amount.slice(1) : `-${amount}`;
}
function signedMoney(value) {
  const zero = !/[1-9]/.test(value.amount);
  return `${zero ? "" : value.amount.startsWith("-") ? "−" : "+"}${formatMoney({ ...value, amount: value.amount.replace(/^[+-]/, "") })}`;
}
// Diferença entre o primeiro e o último saldo registrados pelo banco (mesma moeda), em decimal exato. Mostra
// como o saldo mudou; não é rendimento: aportes, resgates e impostos também mexem no saldo.
function balanceVariation(points) {
  const valid = points.filter((point) => point.money);
  if (valid.length < 2) return null;
  const first = valid[0]; const last = valid[valid.length - 1];
  const [difference] = sumMoney([last.money, { ...first.money, amount: negate(first.money.amount) }]);
  return difference ? { money: difference, from: first.period, to: last.period } : null;
}
function Evolution({ dataset, points, item, onMore, retry }) {
  const rows = points.filter((p) => p.money).reverse();
  return <Section id="investment-history" title="Evolução do saldo">
    {dataset.status === "loading" ? <Skeleton width="100%" height="10rem" />
      : dataset.status === "error" ? <LoadError message="Não foi possível obter o histórico agora." retry={retry} />
      : <>
        <ObservedInvestmentChart points={points} idPrefix={item.key} />
        {rows.length > 1 && <details className="inv-page__disclosure">
          <summary>Posições registradas ({rows.length})</summary>
          <dl className="inv-observed__list">{rows.map((point) => <div key={point.key}><dt>{point.period}</dt><dd>{formatMoney(point.money)}</dd></div>)}</dl>
        </details>}
        <More dataset={dataset} onMore={onMore} />
      </>}
  </Section>;
}
const MONEY_FIELDS = [
  ["transaction_value", "Valor informado"], ["transaction_gross_value", "Valor bruto"], ["transaction_net_value", "Valor líquido"],
  ["income_tax", "IR"], ["financial_transaction_tax", "IOF"], ["transaction_exit_fee", "Taxa de saída"],
];
function movementAmount(document) {
  for (const field of ["transaction_value", "transaction_gross_value", "transaction_net_value"]) {
    const value = moneyField(document, field);
    if (value) return value;
  }
  return null;
}
function Movements({ dataset, onMore, retry }) {
  return <Section id="investment-movements" title="Movimentações">
    {dataset.status === "loading" ? <Skeleton width="100%" height="5rem" />
      : dataset.status === "error" ? <LoadError message="Não foi possível obter as movimentações." retry={retry} />
      : <>
        {dataset.data.length === 0 && <p className="record-detail__empty">Nenhuma movimentação recebida até agora.</p>}
        {dataset.data.length > 0 && <ul className="inv-page__movements">{dataset.data.map((row) => {
          const amount = movementAmount(row.document);
          const doc = row.document;
          const quotas = "transaction_quota_quantity" in (doc?.availability ?? {});
          const fields = [...MONEY_FIELDS.map(([field, label]) => money(label, doc, field)),
            scalar("Quantidade", doc, quotas ? "transaction_quota_quantity" : "transaction_quantity"),
            money("Preço unitário", doc, "transaction_quota_price" in (doc?.availability ?? {}) ? "transaction_quota_price" : "transaction_unit_price")].filter(Boolean);
          return <li key={row.id}><details className="inv-page__movement">
            <summary>
              <span className="inv-page__movement-name">{MOVEMENT_NAMES[row.movementType] ?? "Outra movimentação"}</span>
              <span className="inv-page__movement-date">{dateLabel(doc?.values?.transaction_conversion_date ?? doc?.values?.transaction_date)}</span>
              <span className={`inv-page__movement-value${amount ? "" : " inv-page__movement-value--muted"}`}>{amount ? formatMoney(amount) : "Não informado"}</span>
            </summary>
            {fields.length ? <dl className="record-detail__grid inv-page__movement-detail">{fields}</dl>
              : <p className="record-detail__empty inv-page__movement-detail">O banco não enviou outros dados desta movimentação.</p>}
          </details></li>;
        })}</ul>}
        <More dataset={dataset} onMore={onMore} />
      </>}
  </Section>;
}
function DetailGroup({ title, children }) {
  const fields = children.flat().filter(Boolean);
  if (!fields.length) return null;
  return <div className="inv-page__group"><h3>{title}</h3><dl className="record-detail__grid">{fields}</dl></div>;
}
function ProductFacts({ item, resource, document }) {
  const contract = resource.contract?.values ?? {};
  const terms = (item.reserved ? document?.values?.available_amount?.length === 1 ? document.values.available_amount[0]?.remuneration?.values
    : null : contract.remuneration?.values) ?? {};
  const funds = item.family === "funds";
  const equity = item.family === "variable-incomes";
  const fixedIncome = FIXED_INCOME.includes(item.family);
  const fields = [
    !item.reserved && scalar(funds ? "Quantidade de cotas" : "Quantidade", document, funds ? "quota_quantity" : "quantity"),
    fixedIncome && money("Preço unitário de aquisição", document, "purchase_unit_price"),
    !item.reserved && money(funds ? "Preço da cota" : equity ? "Último fechamento" : "Preço unitário atualizado", document,
      funds ? "quota_gross_price_value" : equity ? "closing_price" : "updated_unit_price"),
    (fixedIncome || item.reserved) && text("Indexador", terms.indexer),
    (fixedIncome || item.reserved) && rate("Percentual do indexador", terms.post_fixed_indexer_percentage),
    (fixedIncome || item.reserved) && rate("Taxa prefixada ou adicional", terms.pre_fixed_rate),
    fixedIncome && date("Aplicação", contract.purchase_date),
    fixedIncome && date("Vencimento", contract.due_date),
    fixedIncome && date("Fim da carência", contract.grace_period_date),
  ].filter(Boolean);
  if (!fields.length) return null;
  return <Section id="investment-facts" title="Informações do produto"><dl className="record-detail__grid">{fields}</dl></Section>;
}
function InvestmentDetails({ item, resource, document }) {
  const contract = resource.contract?.values ?? {};
  const terms = contract.remuneration?.values ?? {};
  const identity = resource.identity?.values ?? {};
  const funds = item.family === "funds";
  const fixedIncome = FIXED_INCOME.includes(item.family);
  const amounts = item.reserved ? document?.values?.available_amount ?? [] : [];
  const groups = item.reserved ? amounts.map((amount, index) => {
    const values = amount?.remuneration?.values ?? {};
    return <DetailGroup key={index} title={amounts.length > 1 ? `Remuneração do saldo ${index + 1} · ${formatMoney(amount)}` : "Remuneração"}>{[
      ...(amounts.length > 1 ? [text("Indexador", values.indexer), rate("Taxa prefixada ou adicional", values.pre_fixed_rate),
        rate("Percentual do indexador", values.post_fixed_indexer_percentage)] : []),
      text("Periodicidade da taxa", PERIODICITY[values.rate_periodicity]),
    ]}</DetailGroup>;
  }) : [
    fixedIncome && <DetailGroup key="terms" title="Remuneração">{[
      text("Periodicidade da taxa", PERIODICITY[terms.rate_periodicity]),
      text("Capitalização", RATE_TYPE[terms.rate_type]), text("Convenção de dias", DAY_COUNT[terms.calculation]),
    ]}</DetailGroup>,
    fixedIncome && <DetailGroup key="dates" title="Prazos e resgate">{[
      date("Emissão", contract.issue_date), text("Pagamento de cupons", YES_NO[contract.voucher_payment_indicator]),
      text("Isento de IR", YES_NO[contract.tax_exempt_product]),
    ]}</DetailGroup>,
    <DetailGroup key="position" title="Impostos e outros valores da posição">{[
      item.family === "variable-incomes" && scalar("Fator de preço", document, "price_factor"),
      ...(funds ? [money("Provisão de IR", document, "income_tax_provision"), money("Provisão de IOF", document, "financial_transaction_tax_provision")]
        : [money("IR informado na posição", document, "income_tax"), money("IOF informado na posição", document, "financial_transaction_tax")]),
    ]}</DetailGroup>,
    <DetailGroup key="identity" title="Identificação">{[
      text("Ativo", identity.ticker), text("Código ISIN", identity.isin_code), money("Preço de emissão", resource.contract, "issue_unit_price"),
      ...(funds ? [text("Categoria ANBIMA", identity.anbima_category), text("Classe", identity.anbima_class), text("Subclasse", identity.anbima_subclass)] : []),
    ]}</DetailGroup>,
  ];
  const visible = groups.filter(Boolean);
  return <details className="inv-page__details">
    <summary><h2>Detalhes do investimento</h2><span className="inv-page__details-chevron" aria-hidden="true"><ChevronDownIcon /></span></summary>
    <div className="inv-page__groups">{visible}<p className="record-detail__empty inv-page__groups-empty">O banco não enviou outros detalhes deste investimento.</p></div>
  </details>;
}
export default function InvestmentDetail() {
  const { investmentId, reserveId } = useParams();
  const reserved = Boolean(reserveId);
  const id = reserveId ?? investmentId;
  const owner = useAuth()?.user?.id;
  const { accounts, connections, connectors, effectiveConnectionId, dataRefreshKey } = useAccount();
  const planning = usePlanning();
  const target = useMemo(() => ({ id, reserved, key: `${reserved ? "reserved" : "investment"}:${id}` }), [id, reserved]);
  const state = useInvestmentResource(target, owner, dataRefreshKey);
  const resource = id && state.resource?.id?.toLowerCase() === id.toLowerCase() ? state.resource : null;
  const item = useMemo(() => {
    if (!resource) return null;
    const withLogos = connections.map((connection) => ({ ...connection,
      institutionLogoUrl: connectors.find((connector) => connector.id === connection.institutionId)?.logoUrl ?? null }));
    const normalized = normalizeInvestment(resource, reserved, withLogos);
    return { ...normalized, accountNumber: reserved ? accounts.find((account) => account.id === resource.bankAccountId)?.number ?? null : null,
      institution: { ...normalized.institution, name: resource.institution?.institutionName || normalized.institution.name } };
  }, [resource, reserved, connections, connectors, accounts]);
  const primaryField = useMemo(() => {
    if (reserved) return "available_amount";
    const position = resource?.position?.document;
    return !moneyField(position, "gross_amount", true) && moneyField(position, "net_amount", true) ? "net_amount" : "gross_amount";
  }, [resource, reserved]);
  const points = useMemo(() => {
    const all = historyPoints(state.history.data, reserved, primaryField);
    const currency = [...all].reverse().find((point) => point.money)?.money.currency;
    return all.map((point) => ({ ...point, money: point.money?.currency === currency ? point.money : null }));
  }, [state.history.data, reserved, primaryField]);
  const variation = useMemo(() => balanceVariation(points), [points]);
  usePageTitle(item?.name ?? "Investimento");
  useEffect(() => { window.scrollTo({ top: 0, behavior: "instant" }); }, [id]);
  const openedScope = useRef(effectiveConnectionId);

  const back = <Link to={LIST_PATH} className="record-detail__back">‹ Investimentos</Link>;
  if (item && effectiveConnectionId && effectiveConnectionId !== openedScope.current && item.integrationId !== effectiveConnectionId) return <Navigate to={LIST_PATH} replace />;
  if (state.error && !resource) return <div className="record-detail inv-page">{back}
    <p className="record-detail__error" role="alert">{state.removed ? "Este investimento não está mais disponível." : "Não foi possível carregar este investimento agora."}</p>
    {!state.removed && <TextButton onClick={state.retry}>Tentar novamente</TextButton>}
  </div>;
  if (!item) return <div className="record-detail inv-page" aria-busy="true">{back}<Skeleton width="16rem" height="1.8rem" /><Skeleton width="100%" height="5rem" /><Skeleton width="100%" height="10rem" /></div>;

  const document = item.reserved ? resource.document : resource.position?.document;
  const gross = moneyField(document, "gross_amount");
  const net = moneyField(document, "net_amount");
  const priorGross = !gross && !net && moneyField(document, "gross_amount", true);
  const priorNet = !gross && !net && !priorGross && moneyField(document, "net_amount", true);
  const reservedAmounts = item.reserved ? (document?.values?.available_amount ?? []).filter(validMoney) : [];
  const current = item.reserved ? reservedAmounts : [gross ?? net ?? priorGross ?? priorNet].filter(Boolean);
  const lastKnown = item.reserved ? document?.availability?.available_amount !== "available" && reservedAmounts.length > 0 : !gross && !net && current.length > 0;
  const goal = planning.status === "ready" ? goalOf(item, planning.goals) : null;
  const purpose = planning.status !== "ready" ? null : isReserve(item, planning.reserve) ? "Reserva de emergência" : goal ? `Meta “${goal.name}”` : null;
  const reference = item.reserved ? null : document?.values?.reference_date_time ?? document?.values?.reference_date ?? resource.position?.referenceAt;
  const received = dateLabel(resource.lastObservedAt ?? item.lastObservedAt);
  const actualStatus = ({ EXPIRED: "expired", REVOKED: "revoked", REJECTED: "rejected", AWAITING_AUTHORIZATION: "pending_authorization" })[resource.sourceConnectionStatus]
    ?? ({ EXPIRED: "expired", REVOKED: "revoked", REJECTED: "rejected" })[resource.sourceAuthorization?.providerStatus] ?? item.state;
  const notices = [
    actualStatus !== "available" && (STATE_TEXT[actualStatus] ?? "Aguardando confirmação dos dados."),
    item.catalogPresence?.status === "not_reported_in_last_full_scan" && "Não informado na última atualização do banco.",
    state.throttled && "Muitas consultas agora. Os dados exibidos são os da última atualização.",
    resource.synchronization?.some((s) => s.status === "Error" || s.status === "Blocked") && "Parte dos dados não pôde ser atualizada.",
  ].filter(Boolean);

  return <div className="record-detail inv-page">
    {back}
    <header className="record-detail__header">
      <div className="record-detail__identity">
        <div className="record-detail__name-row"><InvestmentIcon item={item} /><h1>{item.name}</h1></div>
        <p className="record-detail__meta inv-page__meta">
          <span>{item.modality.label}</span><span aria-hidden="true">·</span>
          <span className="inv-page__institution"><InstitutionLogo institution={item.institution} />{item.institution.name}</span>
          {item.accountNumber && <><span aria-hidden="true">·</span><span>conta <span className="inv-row__account">{item.accountNumber}</span></span></>}
          {purpose && <><span aria-hidden="true">·</span><span className="inv-page__purpose">{purpose}</span></>}
        </p>
      </div>
      <div className="record-detail__header-actions inv-page__actions">
        <TextButton onClick={state.retry} disabled={state.loading}>{state.loading ? "Atualizando…" : "Atualizar"}</TextButton>
      </div>
    </header>

    <section className="inv-page__summary" aria-label="Resumo do investimento">
      <dl className="inv-page__figures">
        <Figure label="Valor atual" values={current.map(formatMoney)} note={lastKnown ? "Último valor informado" : null} />
        {variation && <div className="inv-page__figure">
          <dt>Variação do saldo</dt>
          <dd className="inv-page__figure-value inv-page__figure-value--secondary">{signedMoney(variation.money)}</dd>
          <dd className="inv-page__figure-note">De {variation.from} a {variation.to}</dd>
        </div>}
      </dl>
      <p className="inv-page__updated">{reference ? `Posição de ${dateLabel(reference)} · recebida em ${received}` : `Recebido em ${received}`}</p>
      {notices.map((notice) => <p key={notice} className="inv-page__notice" role="status">{notice}</p>)}
    </section>

    <Evolution dataset={state.history} points={points} item={item} onMore={() => state.more("history")} retry={state.retry} />
    <ProductFacts item={item} resource={resource} document={document} />
    {!item.reserved && <Movements dataset={state.movements} onMore={() => state.more("movements")} retry={state.retry} />}
    <InvestmentDetails item={item} resource={resource} document={document} />
  </div>;
}
