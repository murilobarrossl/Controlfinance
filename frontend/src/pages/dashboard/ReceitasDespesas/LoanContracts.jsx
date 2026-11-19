import { useEffect, useState } from "react";
import { Link } from "react-router";
import { getLoanContracts } from "../../../api/loans.js";
import { isAbortError } from "../../../api/client.js";
import { useAccount } from "../../../context/AccountContext.jsx";
import { formatCurrency, formatDate } from "../../../utils/financeMath.js";
import { isoToBr } from "../../../utils/dates.js";

const enumLabel = (value) =>
  value ? value.charAt(0) + value.slice(1).toLowerCase().replaceAll("_", " ") : null;

function instalmentProgress(v) {
  const { paidInstalments: paid, totalInstalments: total } = v;
  if (paid == null || total == null || total <= 0) return null;
  return { paid, total, ratio: Math.min(1, Math.max(0, paid / total)) };
}

function contractFields(v, hasProgress) {
  return [
    ["Contratado em", v.contractDate && isoToBr(v.contractDate)],
    ["Valor contratado", v.contractAmount != null && formatCurrency(v.contractAmount)],
    ["Próxima parcela", v.nextInstalmentAmount != null && formatCurrency(v.nextInstalmentAmount)],
    ["CET ao ano", v.cet != null && `${(v.cet * 100).toLocaleString("pt-BR", { maximumFractionDigits: 2 })}%`],
    ["Periodicidade", enumLabel(v.instalmentPeriodicity)],
    ["Último vencimento", v.dueDate && isoToBr(v.dueDate)],
    ["Parcelas pagas", !hasProgress && v.paidInstalments != null && String(v.paidInstalments)],
    ["Parcelas em atraso", !hasProgress && v.pastDueInstalments > 0 && String(v.pastDueInstalments)],
  ].filter(([, value]) => value);
}

function LoanContract({ contract }) {
  const v = contract.values ?? {};
  const progress = instalmentProgress(v);
  const updatedAt = contract.providerUpdatedAt ?? contract.observedAt;
  const fields = contractFields(v, Boolean(progress));
  const titleId = `rd-loan-${contract.id}`;

  return (
    <li className="rd-loan">
      <div className="rd-loan__head">
        <div className="rd-loan__identity">
          <h3 id={titleId} className="rd-loan__title">
            {v.productName || enumLabel(v.productSubType) || "Empréstimo"}
          </h3>
          <p className="rd-loan__meta">
            {contract.institutionName && <span>{contract.institutionName}</span>}
            {contract.institutionName && updatedAt && <span className="rd-loan__dot" aria-hidden="true" />}
            {updatedAt && <span>Atualizado em {formatDate(updatedAt)}</span>}
          </p>
        </div>

        {v.outstandingBalance != null && (
          <dl className="rd-loan__balance">
            <dt>Saldo devedor</dt>
            <dd>{formatCurrency(v.outstandingBalance)}</dd>
          </dl>
        )}
      </div>

      {progress && (
        <div className="rd-loan__progress">
          <span className="rd-loan__track" aria-hidden="true">
            <span className="rd-loan__fill" style={{ width: `${progress.ratio * 100}%` }} />
          </span>
          <p className="rd-loan__progress-text">
            <span>
              {progress.paid} de {progress.total} parcelas pagas
            </span>
            {v.pastDueInstalments > 0 && (
              <span className="rd-loan__overdue">
                {v.pastDueInstalments} em atraso
              </span>
            )}
          </p>
        </div>
      )}

      {fields.length > 0 && (
        <dl className="rd-loan__fields">
          {fields.map(([label, value]) => (
            <div key={label} className="rd-loan__field">
              <dt>{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      )}
    </li>
  );
}

export default function LoanContracts() {
  const { dataRefreshKey } = useAccount();
  const [data, setData] = useState(null);

  // Recarrega junto com o resto da tela quando um sync traz dados novos.
  useEffect(() => {
    const controller = new AbortController();
    getLoanContracts({ signal: controller.signal })
      .then(setData)
      .catch((err) => {
        if (!isAbortError(err) && !controller.signal.aborted) setData({ failed: true });
      });
    return () => controller.abort();
  }, [dataRefreshKey]);

  if (!data) return null;
  // Falha não é "sem empréstimos": a seção aparece com o erro.
  if (data.failed)
    return (
      <section className="rd-loans" aria-labelledby="rd-loans-title">
        <header className="rd-loans__header">
          <h2 id="rd-loans-title" className="rd-loans__title">Empréstimos</h2>
        </header>
        <p className="receitas-despesas__error">Não foi possível carregar os contratos de empréstimo.</p>
      </section>
    );
  const contracts = data.contracts ?? [];
  if (contracts.length === 0 && data.connectionsWithoutAuthorization === 0 && data.authorizedConnections === 0) return null;

  return (
    <section className="rd-loans" aria-labelledby="rd-loans-title">
      <header className="rd-loans__header">
        <h2 id="rd-loans-title" className="rd-loans__title">
          Empréstimos
        </h2>
        {contracts.length > 0 && (
          <span className="rd-loans__count">
            {contracts.length} contrato{contracts.length === 1 ? "" : "s"}
          </span>
        )}
      </header>

      {contracts.length > 0 ? (
        <ul className="rd-loans__list">
          {contracts.map((c) => (
            <LoanContract key={c.id} contract={c} />
          ))}
        </ul>
      ) : (
        data.authorizedConnections > 0 && <p className="rd-loans__note">Nenhum contrato informado pelo banco.</p>
      )}

      {data.connectionsWithoutAuthorization > 0 && (
        <p className="rd-loans__note">
          Para ver contratos de outros bancos, conecte o banco de novo em <Link to="/conectar-banco">Conectar banco</Link>{" "}
          e autorize operações de crédito.
        </p>
      )}
    </section>
  );
}
