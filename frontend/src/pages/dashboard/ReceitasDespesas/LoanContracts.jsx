import { useEffect, useState } from "react";
import { Link } from "react-router";
import { getLoanContracts } from "../../../api/loans.js";
import { isAbortError } from "../../../api/client.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import { isoToBr } from "../../../utils/dates.js";

const enumLabel = (value) =>
  value ? value.charAt(0) + value.slice(1).toLowerCase().replaceAll("_", " ") : null;

function contractFields(v) {
  const instalments =
    v.paidInstalments != null && v.totalInstalments != null ? `${v.paidInstalments} de ${v.totalInstalments} pagas` : null;
  return [
    ["Contratado em", v.contractDate && isoToBr(v.contractDate)],
    ["Valor contratado", v.contractAmount != null && formatCurrency(v.contractAmount)],
    ["CET ao ano", v.cet != null && `${(v.cet * 100).toLocaleString("pt-BR", { maximumFractionDigits: 2 })}%`],
    ["Parcelas", instalments],
    ["Parcelas em atraso", v.pastDueInstalments > 0 && String(v.pastDueInstalments)],
    ["Próxima parcela", v.nextInstalmentAmount != null && formatCurrency(v.nextInstalmentAmount)],
    ["Periodicidade", enumLabel(v.instalmentPeriodicity)],
    ["Saldo devedor", v.outstandingBalance != null && formatCurrency(v.outstandingBalance)],
    ["Último vencimento", v.dueDate && isoToBr(v.dueDate)],
  ].filter(([, value]) => value);
}

export default function LoanContracts() {
  const [data, setData] = useState(null);

  useEffect(() => {
    const controller = new AbortController();
    getLoanContracts({ signal: controller.signal })
      .then(setData)
      .catch((err) => {
        if (!isAbortError(err)) setData({ failed: true });
      });
    return () => controller.abort();
  }, []);

  if (!data || data.failed) return null;
  const contracts = data.contracts ?? [];
  if (contracts.length === 0 && data.connectionsWithoutAuthorization === 0 && data.authorizedConnections === 0) return null;

  return (
    <section className="rd-loans" aria-labelledby="rd-loans-label">
      <h2 id="rd-loans-label" className="rd-result__label">
        Contratos de empréstimo
      </h2>
      <p className="rd-result__text">
        Informativo: contratos não entram nos totais acima. O valor recebido e as parcelas pagas aparecem nas
        movimentações da conta.
      </p>

      {contracts.length === 0 && data.authorizedConnections > 0 && (
        <p className="rd-result__text">Nenhum contrato informado pelo banco.</p>
      )}

      {data.connectionsWithoutAuthorization > 0 && (
        <p className="rd-result__text">
          Para ver contratos de outros bancos, conecte o banco de novo em <Link to="/conectar-banco">Conectar banco</Link>{" "}
          e autorize operações de crédito.
        </p>
      )}

      {contracts.length > 0 && (
        <ul className="rd-loans__list">
          {contracts.map((c) => (
            <li key={c.id} className="rd-loans__item">
              <p className="rd-loans__title">
                {c.values?.productName || enumLabel(c.values?.productSubType) || "Empréstimo"}
                {c.institutionName && <span className="rd-loans__bank"> · {c.institutionName}</span>}
              </p>
              <dl className="rd-loans__fields">
                {contractFields(c.values ?? {}).map(([label, value]) => (
                  <div key={label}>
                    <dt>{label}</dt>
                    <dd>{value}</dd>
                  </div>
                ))}
              </dl>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
