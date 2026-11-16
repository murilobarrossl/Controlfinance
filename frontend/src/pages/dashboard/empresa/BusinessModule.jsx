import { useId } from "react";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import usePageTitle from "../../../hooks/usePageTitle.js";
import "./BusinessModule.css";

const UNAVAILABLE = "Ainda não disponível.";

// Estrutura comum dos módulos empresariais. Enquanto o módulo não existe (ready = false), o selo
// "Em preparação" fica no título e cada tabela diz "Ainda não disponível." em vez de "Nenhum
// registro": nada é consultado, então não pode parecer uma busca que voltou vazia. Ao implementar
// um módulo, passe ready e o emptyText real de cada tabela.
export default function BusinessModule({ title, ready = false, actions, children }) {
  usePageTitle(title);
  return (
    <div className="biz-module">
      <header className="biz-module__header">
        <SectionHeading title={title} level={1} />
        {!ready && <StatusPill status="Pending">Em preparação</StatusPill>}
        {actions && <div className="biz-module__actions">{actions}</div>}
      </header>
      {children}
    </div>
  );
}

export function ModuleSection({ title, children }) {
  const id = useId();
  return (
    <section className="biz-section" aria-labelledby={id}>
      <h2 id={id} className="biz-section__title">
        {title}
      </h2>
      {children}
    </section>
  );
}

// columns: [{ label, numeric }]. Numéricas alinham à direita, como os valores de Relatórios.
// Sem emptyText, a tabela está no estado "módulo ainda não disponível".
export function ModuleTable({ columns, emptyText }) {
  const unavailable = !emptyText;
  return (
    <div className="biz-table-wrap">
      <table className={`biz-table${unavailable ? " biz-table--unavailable" : ""}`}>
        <thead>
          <tr>
            {columns.map(({ label, numeric }) => (
              <th key={label} scope="col" className={numeric ? "biz-table__numeric" : undefined}>
                {label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          <tr className="biz-table__state">
            <td colSpan={columns.length}>{emptyText ?? UNAVAILABLE}</td>
          </tr>
        </tbody>
      </table>
    </div>
  );
}
