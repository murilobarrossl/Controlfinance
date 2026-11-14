import { useMemo, useState } from "react";
import Button from "../../../components/ui/Button/Button.jsx";
import { ShieldIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { setReserveResource } from "../../../api/planning.js";
import { dateLabel, formatExact, formatMoney, formatContractNumber, goalOf, isReserve, moneyField, STATE_TEXT, validMoney } from "../../../utils/importedInvestments.js";
import { useInvestmentResource } from "./useInvestmentResource.js";
import useSaving from "./useSaving.js";
import InvestmentYieldChart from "./InvestmentYieldChart.jsx";

export const MOVEMENT_NAMES = {
  APLICACAO: "Aplicação", RESGATE: "Resgate", COMPRA: "Compra", VENDA: "Venda", VENCIMENTO: "Vencimento",
  PAGAMENTO_JUROS: "Pagamento de juros", AMORTIZACAO: "Amortização", DIVIDENDOS: "Dividendos", JCP: "Juros sobre capital próprio",
  ALUGUEIS: "Aluguel de ativos", COME_COTAS: "Come-cotas", CANCELAMENTO: "Cancelamento", PREMIO: "Prêmio", MULTA: "Multa", MORA: "Juros de mora",
  TRANSFERENCIA_COTAS: "Transferência de cotas", TRANSFERENCIA_CUSTODIA: "Transferência de custódia", TRANSFERENCIA_TITULARIDADE: "Transferência de titularidade",
};
const MONTH_NAMES = new Intl.DateTimeFormat("pt-BR", { month: "long", year: "numeric", timeZone: "UTC" });
export function currentMonth() {
  const now = new Date(); return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`;
}
function MoneyRow({ label, document, field, className }) {
  const money = moneyField(document, field);
  const prior = !money && moneyField(document, field, true);
  const evidence = document?.provenance?.[field];
  return <div className={className}><dt>{label}</dt><dd>{formatMoney(money)}{prior && <small className="inv-real__prior">Último informado: {formatMoney(prior)}{evidence?.observed_at ? ` · coleta em ${dateLabel(evidence.observed_at)}` : ""}</small>}</dd></div>;
}
function TextRow({ label, value }) { return <div><dt>{label}</dt><dd>{value || "Não informado"}</dd></div>; }
function ScalarRow({ label, document, field }) {
  return <TextRow label={label} value={document?.availability?.[field] === "available" ? formatExact(document.values?.[field]) : null} />;
}
function ReserveResourceButton({ item, planning }) {
  const { saving, error, run } = useSaving();
  const goal = goalOf(item, planning.goals);
  const selected = isReserve(item, planning.reserve, planning.goals);
  if (goal) return <p className="inv-detail__updated">Vinculado à meta “{goal.name}”</p>;
  return <div className="inv-real__reserve-action">
    <Button as="button" type="button" variant="outline" size="sm" onClick={() => run(async () => planning.replace(await setReserveResource(item, !selected)))}
      disabled={saving || planning.status !== "ready"} aria-pressed={selected}>
      <ShieldIcon />{saving ? "Salvando…" : selected ? "Tirar da reserva de emergência" : "Usar como reserva de emergência"}
    </Button>
    {error && <p className="investimentos__error" role="alert">{error}</p>}
  </div>;
}
function More({ dataset, onMore }) {
  return <>{dataset.moreError && <p className="investimentos__error" role="alert">Não foi possível carregar a próxima página.</p>}
    {dataset.cursor && <Button as="button" type="button" variant="outline" size="sm" disabled={dataset.loadingMore || dataset.refreshing} onClick={onMore}>{dataset.loadingMore || dataset.refreshing ? "Carregando…" : "Carregar mais registros"}</Button>}</>;
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
function RealHistory({ dataset, item, onMore, retry }) {
  const [field, setField] = useState("gross_amount");
  const [currencyChoice, setCurrency] = useState("");
  const points = useMemo(() => historyPoints(dataset.data, item.reserved, field), [dataset.data, item.reserved, field]);
  const currencies = [...new Set(points.map((p) => p.money?.currency).filter(Boolean))];
  const currency = currencies.includes(currencyChoice) ? currencyChoice : currencies[0];
  const compatible = points.map((p) => ({ ...p, money: p.money?.currency === currency ? p.money : null }));
  return <section className="inv-detail__section">
    <div className="inv-detail__section-head"><h4 className="inv-detail__title">{item.reserved ? "Evolução do saldo" : "Evolução da posição"}</h4>
      {!item.reserved && <label className="inv-real__select">Saldo<select value={field} onChange={(e) => setField(e.target.value)}><option value="gross_amount">Bruto</option><option value="net_amount">Líquido</option></select></label>}
      {currencies.length > 1 && <label className="inv-real__select">Moeda<select value={currency} onChange={(e) => setCurrency(e.target.value)}>{currencies.map((c) => <option key={c}>{c}</option>)}</select></label>}
    </div>
    {dataset.status === "loading" ? <p className="investimentos__hint" role="status">Carregando histórico…</p>
      : dataset.status === "error" ? <div className="investimentos__failure" role="alert"><p className="investimentos__hint">Não foi possível obter o histórico agora.</p><button type="button" className="investimentos__link-button" onClick={retry}>Tentar novamente</button></div>
      : <>
        <InvestmentYieldChart kind="historical" points={compatible} idPrefix={item.key} />
        {item.reserved && <p className="investimentos__fineprint">As datas indicam quando cada saldo foi recebido.</p>}
        <details className="investimentos__explain"><summary>Ver posições registradas ({dataset.data.length})</summary>
          <div className="investimentos__table-wrap"><table className="investimentos__table"><thead><tr><th>{item.reserved ? "Registro" : "Referência"}</th><th>Valor informado</th><th>Coleta</th></tr></thead><tbody>
            {dataset.data.map((row) => {
              const amounts = item.reserved ? (row.payload?.values?.available_amount ?? []).filter(validMoney) : [moneyField(row.payload, field)].filter(Boolean);
              return <tr key={row.id}><td>{item.reserved ? dateLabel(row.observedAt) : dateLabel(row.payload?.values?.reference_date_time ?? row.payload?.values?.reference_date ?? row.referenceAt)}</td>
                <td>{amounts.length ? amounts.map((money, index) => <span className="inv-real__amount" key={index}>{formatMoney(money)}</span>) : "Não informado"}</td><td>{dateLabel(row.observedAt)}</td></tr>;
            })}
          </tbody></table></div>
          {dataset.data.length === 0 && <p className="investimentos__hint">Ainda não há posições registradas.</p>}
        </details>
        <More dataset={dataset} onMore={onMore} />
        {dataset.cursor && <p className="investimentos__hint">O gráfico mostra só os registros já carregados. Carregue mais para ampliar o período.</p>}
      </>}
  </section>;
}
const MONEY_FIELDS = [
  ["transaction_value", "Valor informado"], ["transaction_gross_value", "Valor bruto"], ["transaction_net_value", "Valor líquido"],
  ["income_tax", "IR informado na operação"], ["financial_transaction_tax", "IOF informado na operação"], ["transaction_exit_fee", "Taxa de saída"],
];
function MonthlyResults({ state, month, setMonth }) {
  const result = state.data;
  const period = /^\d{4}-\d{2}$/.test(month) ? MONTH_NAMES.format(new Date(`${month}-01T00:00:00Z`)) : "mês selecionado";
  return <section className="inv-detail__section">
    <div className="inv-detail__section-head"><h4 className="inv-detail__title">Rendimento mês a mês</h4><label className="inv-real__select">Mês<input type="month" value={month} min="1901-01" max={currentMonth()} onChange={(e) => { if (/^\d{4}-\d{2}$/.test(e.target.value) && e.target.value <= currentMonth()) setMonth(e.target.value); }} /></label></div>
    {state.status === "loading" ? <p className="investimentos__hint" role="status">Consultando os dados de {period}…</p>
      : state.status === "error" ? <p className="investimentos__hint" role="alert">Não foi possível consultar os resultados deste período. Atualize o detalhamento para tentar novamente.</p>
      : <>
        <dl className="inv-real__rows">
          <TextRow label={`Rendimento bruto · ${period}`} value={result?.monthlyGrossIncome?.status !== "unavailable" ? formatMoney(result?.monthlyGrossIncome?.value) : "Não determinado"} />
          <TextRow label={`Rendimento líquido · ${period}`} value={result?.monthlyNetIncome?.status !== "unavailable" ? formatMoney(result?.monthlyNetIncome?.value) : "Não determinado"} />
        </dl>
        <p className="investimentos__fineprint">Variações de saldo também incluem aportes, resgates, amortizações e impostos.</p>
        {result?.operations?.length > 0 && <details className="investimentos__explain"><summary>Operações recebidas em {period}</summary>
          <p className="investimentos__hint">O banco pode não ter enviado todas as operações do mês.</p>
          {result.operations.map((group) => <div className="inv-real__operation" key={group.sourceType}>
            <strong>{MOVEMENT_NAMES[group.sourceType] ?? "Outra movimentação informada"} · {group.count} {group.count === 1 ? "registro" : "registros"}</strong>
            <dl className="inv-real__rows">{MONEY_FIELDS.filter(([field]) => group.values?.[field]).map(([field, label]) => <TextRow key={field} label={label}
              value={group.values[field].status === "sum_of_reported_operations" ? group.values[field].value.map(formatMoney).join(" · ") : "Não determinado"} />)}</dl>
          </div>)}
        </details>}
        {result?.operations?.length === 0 && <p className="investimentos__hint">Nenhuma operação recebida neste mês, o que não significa rendimento zero.</p>}
        {result?.observedIntervalVariation?.status === "derived_from_observed_positions" && <details className="investimentos__explain"><summary>Variação entre posições observadas</summary>
          <p className="investimentos__hint">De {dateLabel(result.observedIntervalVariation.fromReferenceAt)} a {dateLabel(result.observedIntervalVariation.toReferenceAt)}. Este intervalo pode não coincidir com o mês completo.</p>
          <dl className="inv-real__rows"><TextRow label="Variação do saldo bruto" value={formatMoney(result.observedIntervalVariation.gross?.value)} /><TextRow label="Variação do saldo líquido" value={formatMoney(result.observedIntervalVariation.net?.value)} /></dl>
          <p className="investimentos__hint">A variação inclui movimentações e mudanças de preço, por isso não é tratada como rendimento.</p>
        </details>}
      </>}
  </section>;
}
function Movements({ dataset, onMore, retry }) {
  return <section className="inv-detail__section"><h4 className="inv-detail__title">Movimentações</h4>
    {dataset.status === "loading" ? <p className="investimentos__hint" role="status">Carregando movimentações…</p> : dataset.status === "error" ? <div className="investimentos__failure" role="alert"><p className="investimentos__hint">Não foi possível obter as movimentações.</p><button type="button" className="investimentos__link-button" onClick={retry}>Tentar novamente</button></div> : <>
      {dataset.data.length === 0 && <p className="investimentos__hint">Nenhuma movimentação recebida até agora.</p>}
      {dataset.data.map((row) => <details key={row.id} className="investimentos__explain inv-real__operation"><summary>{MOVEMENT_NAMES[row.movementType] ?? "Outra movimentação informada"} · {dateLabel(row.document?.values?.transaction_conversion_date ?? row.document?.values?.transaction_date)}</summary>
        <dl className="inv-real__rows">{MONEY_FIELDS.filter(([field]) => field in (row.document?.availability ?? {})).map(([field, label]) => <MoneyRow key={field} label={label} document={row.document} field={field} />)}
          <ScalarRow label="Quantidade movimentada" document={row.document} field={"transaction_quota_quantity" in (row.document?.availability ?? {}) ? "transaction_quota_quantity" : "transaction_quantity"} />
          <MoneyRow label="Preço unitário da operação" document={row.document} field={"transaction_quota_price" in (row.document?.availability ?? {}) ? "transaction_quota_price" : "transaction_unit_price"} />
        </dl>
      </details>)}
      <More dataset={dataset} onMore={onMore} />
    </>}
  </section>;
}
export default function ImportedInvestmentDetail({ item, planning, owner }) {
  const [month, setMonth] = useState(currentMonth);
  const state = useInvestmentResource(item, owner, month);
  const resource = state.resource;
  const document = item.reserved ? resource?.document : resource?.position?.document;
  const contract = resource?.contract?.values ?? {};
  const terms = contract.remuneration?.values ?? {};
  const identity = resource?.identity?.values ?? {};
  const fixedIncome = ["bank-fixed-incomes", "credit-fixed-incomes", "treasure-titles"].includes(item.family);
  const gross = moneyField(document, "gross_amount");
  const net = moneyField(document, "net_amount");
  const balanceHeading = item.reserved ? "Saldo reservado" : gross ? "Saldo bruto" : net ? "Saldo líquido" : "Saldo não informado";
  const actualStatus = ({ EXPIRED: "expired", REVOKED: "revoked", REJECTED: "rejected", AWAITING_AUTHORIZATION: "pending_authorization" })[resource?.sourceConnectionStatus]
    ?? ({ EXPIRED: "expired", REVOKED: "revoked", REJECTED: "rejected" })[resource?.sourceAuthorization?.providerStatus] ?? item.state;
  return <div className="inv-detail inv-real">
    <div className="inv-detail__top">
      <p className="inv-detail__updated">Dados recebidos em {dateLabel(resource?.lastObservedAt ?? item.lastObservedAt)}</p>
      <div className="inv-detail__actions"><button type="button" className="investimentos__link-button" onClick={state.retry} disabled={state.loading}>Atualizar detalhamento</button><ReserveResourceButton item={item} planning={planning} /></div>
    </div>
    {actualStatus !== "available" && <p className="investimentos__hint" role="status">{STATE_TEXT[actualStatus] ?? "Aguardando confirmação dos dados."}</p>}
    {resource?.synchronization?.some((s) => s.status === "Error" || s.status === "Blocked") && <p className="investimentos__hint" role="status">Parte dos dados não pôde ser atualizada.</p>}
    {state.loading && !resource ? <p className="investimentos__hint" role="status">Carregando detalhamento…</p> : state.error ? <p className="investimentos__hint" role="alert">{state.removed ? "Este investimento não está mais disponível nesta conexão. Atualize a lista de investimentos." : "Não foi possível carregar este investimento agora."}</p> : resource && <>
      <header className="inv-hero">
        <h4 className="inv-hero__label">{balanceHeading}</h4>
        {item.reserved ? (document?.values?.available_amount?.length ? document.values.available_amount.map((money, index) => <strong className="inv-hero__value" key={index}>{formatMoney(money)}</strong>) : <strong className="inv-hero__value">Não informado</strong>) : <strong className="inv-hero__value">{formatMoney(gross ?? net)}</strong>}
        <p className="inv-hero__meta">{item.reserved ? "Sem data de referência informada pelo banco" : `Posição de ${dateLabel(document?.values?.reference_date_time ?? document?.values?.reference_date ?? resource.position?.referenceAt)}, informada pela instituição`}</p>
        {item.reserved && document?.availability?.available_amount !== "available" && <p className="inv-hero__meta">Último saldo informado{document?.provenance?.available_amount?.observed_at ? ` · registrado em ${dateLabel(document.provenance.available_amount.observed_at)}` : ""}</p>}
      </header>
      <div className={`inv-detail__columns${item.reserved ? " inv-detail__columns--single" : ""}`}>
        {!item.reserved && <section className="inv-detail__group" aria-labelledby={`${item.key}-composition`}>
          <h4 id={`${item.key}-composition`} className="inv-detail__title">Composição do saldo</h4>
          <dl className="inv-breakdown__rows">
            <MoneyRow label="Saldo bruto" document={document} field="gross_amount" />
            {item.family === "funds" ? <><MoneyRow label="Provisão de IR" document={document} field="income_tax_provision" /><MoneyRow label="Provisão de IOF" document={document} field="financial_transaction_tax_provision" /></>
              : <><MoneyRow label="IR informado na posição" document={document} field="income_tax" /><MoneyRow label="IOF informado na posição" document={document} field="financial_transaction_tax" /></>}
            <MoneyRow label="Saldo líquido" document={document} field="net_amount" className="inv-breakdown__total" />
          </dl>
          <p className="investimentos__fineprint">“Não informado” não significa zero. IR e IOF da posição não confirmam um desconto já feito; os impostos de cada operação aparecem nas movimentações.</p>
        </section>}
        <section className="inv-detail__group" aria-labelledby={`${item.key}-terms`}>
          <h4 id={`${item.key}-terms`} className="inv-detail__title">Produto e condições</h4>
          {(fixedIncome || item.reserved) && <p className="investimentos__fineprint">Taxas exibidas como a instituição informou.</p>}
          <dl className="inv-real__rows">
            <TextRow label="Modalidade" value={item.modality.label} /><TextRow label="Instituição" value={resource.institution?.institutionName ?? item.institution.name} />
        {identity.ticker && <TextRow label="Ativo" value={identity.ticker} />}
        {identity.isin_code && <TextRow label="Código ISIN" value={identity.isin_code} />}
        {!item.reserved && <><ScalarRow label={item.family === "funds" ? "Quantidade de cotas" : "Quantidade"} document={document} field={item.family === "funds" ? "quota_quantity" : "quantity"} />
          {fixedIncome && <MoneyRow label="Preço de aquisição por unidade" document={document} field="purchase_unit_price" />}
          <MoneyRow label={item.family === "funds" ? "Preço bruto da cota" : item.family === "variable-incomes" ? "Preço de fechamento informado" : "Preço unitário atualizado"} document={document} field={item.family === "funds" ? "quota_gross_price_value" : item.family === "variable-incomes" ? "closing_price" : "updated_unit_price"} />
          {item.family === "variable-incomes" && <ScalarRow label="Fator de preço" document={document} field="price_factor" />}
          {"issue_unit_price" in (resource.contract?.availability ?? {}) && <MoneyRow label="Preço de emissão" document={resource.contract} field="issue_unit_price" />}
          {item.family === "funds" && <><TextRow label="Categoria ANBIMA" value={identity.anbima_category} /><TextRow label="Classe" value={identity.anbima_class} /><TextRow label="Subclasse" value={identity.anbima_subclass} /></>}
          {fixedIncome && <><TextRow label="Indexador contratado" value={terms.indexer} />
          <TextRow label="Taxa prefixada ou adicional — valor original" value={terms.pre_fixed_rate != null ? formatContractNumber(terms.pre_fixed_rate) : null} />
          <TextRow label="Parcela do indexador — valor original" value={terms.post_fixed_indexer_percentage != null ? formatContractNumber(terms.post_fixed_indexer_percentage) : null} />
          <TextRow label="Periodicidade" value={({ ANUAL: "Anual", MENSAL: "Mensal", DIARIO: "Diária", SEMESTRAL: "Semestral" })[terms.rate_periodicity]} />
          <TextRow label="Capitalização" value={({ LINEAR: "Linear", EXPONENCIAL: "Exponencial" })[terms.rate_type]} />
          <TextRow label="Convenção de dias" value={({ DIAS_UTEIS: "Dias úteis", DIAS_CORRIDOS: "Dias corridos" })[terms.calculation]} />
          {contract.voucher_payment_indicator && <TextRow label="Pagamento de cupons informado" value={({ SIM: "Sim", NAO: "Não" })[contract.voucher_payment_indicator]} />}
          {contract.tax_exempt_product && <TextRow label="Isenção informada no contrato" value={({ SIM: "Sim", NAO: "Não" })[contract.tax_exempt_product]} />}
          {[["purchase_date", "Aquisição"], ["issue_date", "Emissão"], ["due_date", "Vencimento"], ["grace_period_date", "Fim da carência"]].map(([field, label]) => <TextRow key={field} label={label} value={dateLabel(contract[field])} />)}
          </>}
        </>}
          </dl>
        </section>
      </div>
      {item.reserved && <section className="inv-detail__section"><h4 className="inv-detail__title">Remuneração dos saldos reservados</h4>
        {(document?.values?.available_amount ?? []).map((amount, index) => <div className="inv-real__operation" key={index}>
          <strong>Saldo {index + 1} · {formatMoney(amount)}</strong>
          {amount?.remuneration?.values ? <dl className="inv-real__rows">
            <TextRow label="Indexador informado" value={amount.remuneration.values.indexer} />
            <TextRow label="Taxa prefixada ou adicional — valor original" value={formatContractNumber(amount.remuneration.values.pre_fixed_rate)} />
            <TextRow label="Parcela do indexador — valor original" value={formatContractNumber(amount.remuneration.values.post_fixed_indexer_percentage)} />
            <TextRow label="Periodicidade" value={({ ANUAL: "Anual", MENSAL: "Mensal", DIARIO: "Diária", SEMESTRAL: "Semestral" })[amount.remuneration.values.rate_periodicity]} />
          </dl> : <p className="investimentos__hint">A instituição não informou a remuneração deste saldo.</p>}
        </div>)}
        {!document?.values?.available_amount?.length && <p className="investimentos__hint">As condições de remuneração não foram fornecidas.</p>}
      </section>}
      <RealHistory dataset={state.history} item={item} onMore={() => state.more("history")} retry={state.retry} />
      {!item.reserved && <><MonthlyResults state={state.financial} month={month} setMonth={setMonth} /><Movements dataset={state.movements} onMore={() => state.more("movements")} retry={state.retry} /></>}
    </>}
  </div>;
}
