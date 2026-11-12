import { useState } from "react";
import Button from "../../../components/ui/Button/Button.jsx";
import { ShieldIcon } from "../../../components/ui/icons/FeatureIcons.jsx";
import { deleteInvestmentRate, setInvestmentRate, setReserveAccount } from "../../../api/planning.js";
import { formatCurrency, formatPercentage } from "../../../utils/financeMath.js";
import { RATE_KINDS, allowedRateKinds, formatRateDate } from "../../../utils/investmentRates.js";
import InvestmentProjection from "./InvestmentProjection.jsx";
import useSaving from "./useSaving.js";

function formatSyncedAt(value) {
  return new Date(value).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
}

function RateForm({ account, modality, profile, planning, onDone }) {
  const kinds = allowedRateKinds(modality.key);
  const [kind, setKind] = useState(profile?.kind && kinds.includes(profile.kind) ? profile.kind : kinds[0]);
  const [value, setValue] = useState(profile ? String(profile.value) : "");
  const [exempt, setExempt] = useState(profile?.incomeTaxExempt ?? false);
  const { saving, error, setError, run } = useSaving();
  const spec = RATE_KINDS[kind];

  async function handleSubmit(e) {
    e.preventDefault();
    const parsed = Number(String(value).replace(",", "."));
    if (value === "" || !Number.isFinite(parsed) || parsed < spec.min || parsed > spec.max) {
      setError(`Informe um valor entre ${formatPercentage(spec.min)} e ${formatPercentage(spec.max)}.`);
      return;
    }
    const ok = await run(async () => {
      const saved = await setInvestmentRate(account.id, { kind, value: parsed, incomeTaxExempt: exempt });
      planning.setRateProfile(account.id, saved);
    });
    if (ok) onDone?.();
  }

  async function handleRemove() {
    const ok = await run(async () => {
      await deleteInvestmentRate(account.id);
      planning.setRateProfile(account.id, null);
    });
    if (ok) onDone?.();
  }

  return (
    <form className="investimentos__form investimentos__form--compact" onSubmit={handleSubmit}>
      {kinds.length > 1 && (
        <label className="investimentos__field">
          <span className="investimentos__field-label">Como rende</span>
          <select value={kind} onChange={(e) => setKind(e.target.value)}>
            {kinds.map((k) => (
              <option key={k} value={k}>
                {RATE_KINDS[k].label}
              </option>
            ))}
          </select>
        </label>
      )}
      <label className="investimentos__field">
        <span className="investimentos__field-label">{spec.fieldLabel}</span>
        <input
          type="number"
          inputMode="decimal"
          step="any"
          min={spec.min}
          max={spec.max}
          placeholder={spec.placeholder}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          required
        />
        <span className="investimentos__field-help">{spec.help}</span>
      </label>
      <label className="investimentos__check">
        <input type="checkbox" checked={exempt} onChange={(e) => setExempt(e.target.checked)} />
        <span>Isenta de imposto de renda (ex.: LCI, LCA)</span>
      </label>
      <div className="investimentos__actions investimentos__form-row">
        <Button as="button" type="submit" variant="primary" size="sm" disabled={saving}>
          {saving ? "Salvando…" : "Salvar regra"}
        </Button>
        {profile && (
          <Button as="button" type="button" variant="outline" size="sm" onClick={handleRemove} disabled={saving}>
            Apagar regra
          </Button>
        )}
        {onDone && (
          <Button as="button" type="button" variant="outline" size="sm" onClick={onDone} disabled={saving}>
            Cancelar
          </Button>
        )}
      </div>
      {error && (
        <p className="investimentos__error investimentos__error--form" role="alert">
          {error}
        </p>
      )}
    </form>
  );
}

function RuleSummary({ rule, modality, institutionName }) {
  if (rule.source === "imported_contract") return <p className="investimentos__hint">{rule.message}</p>;
  if (rule.status === "needsRate") {
    return (
      <p className="investimentos__hint">
        A conexão com o banco não informa quanto esta aplicação rende. Confira no app {institutionName ? `do ${institutionName}` : "do banco"}{" "}
        e informe a regra abaixo: a projeção usa exatamente ela.
      </p>
    );
  }
  if (rule.status === "unavailable") return null;

  const sourceText =
    rule.source === "legal"
      ? "Regra legal da poupança"
      : rule.source === "indexer"
        ? "Indexador do Tesouro Selic. Informe o adicional do seu título, se houver."
        : `Informada por você${rule.taxed ? "" : " · isenta de IR"}`;

  return (
    <div className="inv-detail__rule">
      <span className="inv-detail__rule-value" style={{ "--modality-color": modality.color }}>
        {rule.label}
      </span>
      <span className="inv-detail__rule-annual">≈ {formatPercentage(rule.annualPct)} ao ano hoje</span>
      <span className="inv-detail__rule-source">
        {sourceText}
        {rule.basis && ` · ${rule.basis.name} do Banco Central em ${formatRateDate(rule.basis.date)}`}
      </span>
    </div>
  );
}

function ReserveControl({ account, planning, reserveAccount }) {
  const { saving, error, run } = useSaving();
  const isReserve = reserveAccount?.id === account.id;

  async function choose(id) {
    await run(async () => planning.setReserve(await setReserveAccount(id)));
  }

  if (planning.status !== "ready") return null;

  return (
    <div className="inv-detail__reserve">
      {isReserve ? (
        <>
          <span className="inv-detail__reserve-flag">
            <ShieldIcon />
            Esta é a sua reserva de emergência
          </span>
          <Button as="button" type="button" variant="outline" size="sm" onClick={() => choose(null)} disabled={saving}>
            {saving ? "Salvando…" : "Deixar de usar como reserva"}
          </Button>
        </>
      ) : (
        <Button as="button" type="button" variant="secondary" size="sm" onClick={() => choose(account.id)} disabled={saving}>
          {saving
            ? "Salvando…"
            : reserveAccount
              ? `Usar como reserva de emergência no lugar de ${reserveAccount.name}`
              : "Marcar como reserva de emergência"}
        </Button>
      )}
      {error && (
        <p className="investimentos__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

export default function InvestmentDetail({ item, rule, planning, reserveAccount, ratesStatus, onRetryRates, isCompany }) {
  const { account, modality, institution } = item;
  const profile = planning.rateProfiles?.[account.id] ?? null;
  const [editing, setEditing] = useState(false);
  const canEditRule = allowedRateKinds(modality.key).length > 0 && planning.status === "ready";
  const showForm = canEditRule && (editing || rule.status === "needsRate");
  const idPrefix = `inv-${account.id}`;

  return (
    <div className="inv-detail">
      <div className="inv-detail__top">
        <p className="inv-detail__updated">
          {account.lastSyncedAt
            ? `Saldo informado pelo banco em ${formatSyncedAt(account.lastSyncedAt)}`
            : "Aguardando a primeira sincronização com o banco"}
        </p>
        <ReserveControl account={account} planning={planning} reserveAccount={reserveAccount} />
      </div>

      <section className="inv-result" aria-labelledby={`${idPrefix}-balance`}>
        <div className="inv-result__balance">
          <span className="inv-result__eyebrow">{account.name}</span>
          <h4 id={`${idPrefix}-balance`}>Saldo atual do investimento</h4>
          <strong>{formatCurrency(account.balance)}</strong>
          <span className="inv-result__source">Saldo informado pelo banco</span>
        </div>
        <div className="inv-result__breakdown">
          <h4 className="inv-detail__title">Seus resultados</h4>
          <dl>
            {["Total aplicado", "Rendimento bruto acumulado", "Desconto de IR", "Desconto de IOF", "Rendimento líquido"].map((label) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>Não informado</dd>
              </div>
            ))}
          </dl>
          <p className="inv-result__notice">
            O banco disponibiliza o saldo, mas não o detalhamento dos rendimentos e impostos desta aplicação.
            Valores não informados não significam zero.
          </p>
        </div>
      </section>

      <section className="inv-detail__section" aria-labelledby={`${idPrefix}-rule`}>
        <div className="inv-detail__section-head">
          <h4 id={`${idPrefix}-rule`} className="inv-detail__title">
            Como rende
          </h4>
          {canEditRule && !showForm && (
            <button type="button" className="investimentos__link-button" onClick={() => setEditing(true)}>
              {profile ? "Alterar regra" : "Informar adicional"}
            </button>
          )}
        </div>

        {ratesStatus === "loading" && rule.status === "unavailable" ? (
          <p className="investimentos__hint">Buscando a taxa oficial no Banco Central…</p>
        ) : (
          <RuleSummary rule={rule} modality={modality} institutionName={institution.name} />
        )}

        {rule.status === "unavailable" && ratesStatus !== "loading" && (
          <div className="investimentos__failure" role="status">
            <p className="investimentos__hint">
              Não foi possível obter agora a taxa {rule.indexer} oficial do Banco Central, então a projeção não é
              exibida.
            </p>
            <Button as="button" type="button" variant="outline" size="sm" onClick={onRetryRates}>
              Tentar de novo
            </Button>
          </div>
        )}

        {planning.status === "error" && (
          <p className="investimentos__hint">Não foi possível carregar a regra salva desta aplicação.</p>
        )}

        {showForm && (
          <RateForm
            key={profile?.updatedAt ?? "new"}
            account={account}
            modality={modality}
            profile={profile}
            planning={planning}
            onDone={rule.status === "needsRate" && !profile ? undefined : () => setEditing(false)}
          />
        )}
      </section>

      <div className="inv-detail__history-note">
        <span className="inv-detail__title">Histórico de rendimentos</span>
        <p>O histórico mensal desta aplicação não está disponível na conexão bancária.</p>
      </div>

      {rule.status === "ready" &&
        (account.balance == null ? (
          <p className="investimentos__hint">O banco não informou o saldo desta aplicação, então não há base para a projeção.</p>
        ) : (
          <InvestmentProjection key={account.id} balance={account.balance} rule={rule} isCompany={isCompany} idPrefix={idPrefix} />
        ))}
    </div>
  );
}
