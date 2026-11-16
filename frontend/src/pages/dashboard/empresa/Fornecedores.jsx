import { useState } from "react";
import { Link, useNavigate } from "react-router";
import BusinessModule, { ModuleSection, ModuleTable } from "./BusinessModule.jsx";
import SupplierFormDialog from "./SupplierFormDialog.jsx";
import { SummaryItem } from "./RegistryParts.jsx";
import useRegistryData, { useDebounced } from "./useRegistryData.js";
import Button from "../../../components/ui/Button/Button.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import Pager from "../../../components/ui/Pager/Pager.jsx";
import { getSuppliers, getSuppliersSummary } from "../../../api/suppliers.js";
import { paymentMethodLabel } from "../../../utils/employees.js";
import "./RegistryList.css";

const PAGE_SIZE = 25;

export default function Fornecedores() {
  const navigate = useNavigate();
  const [searchInput, setSearchInput] = useState("");
  const search = useDebounced(searchInput.trim(), 350);
  const [refreshKey, setRefreshKey] = useState(0);
  const [formOpen, setFormOpen] = useState(false);
  const { summary, summaryError, page, setPage, loaded, listError, shown, refreshing, items, totalCount, totalPages } = useRegistryData({
    loadSummary: getSuppliersSummary,
    loadList: getSuppliers,
    filters: { search },
    refreshKey,
    pageSize: PAGE_SIZE,
    listError: "Não foi possível carregar os fornecedores.",
    summaryError: "Não foi possível carregar o resumo.",
  });
  const noSuppliersAtAll = summary?.totalCount === 0;
  const hasRecords = summary?.totalCount > 0;

  function handleSaved(saved) {
    setFormOpen(false);
    setRefreshKey((k) => k + 1);
    navigate(`/dashboard/fornecedores/${saved.id}`);
  }

  return (
    <BusinessModule
      title="Fornecedores"
      ready
      actions={
        <Button as="button" type="button" variant="outline" size="sm" className="biz-module__register" onClick={() => setFormOpen(true)}>
          Cadastrar fornecedor
        </Button>
      }
    >
      <div className="registry">
        {summaryError ? (
          <p className="registry__error">{summaryError}</p>
        ) : (
          <dl className="registry-summary summary-dividers" aria-busy={!summary}>
            <SummaryItem label="Fornecedores cadastrados" value={summary ? summary.totalCount : <Skeleton width="2.5rem" height="1.6rem" />} />
            <SummaryItem label="Com dados de pagamento" value={summary ? summary.withPaymentCount : <Skeleton width="2.5rem" height="1.6rem" />} />
          </dl>
        )}

          <section className="registry__list" aria-labelledby="suppliers-list-title">
            <h2 id="suppliers-list-title" className="visually-hidden">
              Fornecedores
            </h2>
            {hasRecords && (
              <div className="registry__toolbar">
                <input
                  type="search"
                  className="registry__search"
                  placeholder="Buscar por nome ou razão social"
                  aria-label="Buscar fornecedor por nome, razão social ou nome fantasia"
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  autoComplete="off"
                />
              </div>
            )}

            {listError ? (
              <p className="registry__error">{listError}</p>
            ) : (
              <div className="registry__table-wrap">
                <table className={`biz-table registry__table${refreshing ? " registry__table--refreshing" : ""}`} aria-busy={!loaded}>
                  <thead>
                    <tr>
                      <th scope="col">Fornecedor</th>
                      <th scope="col" className="registry__col--optional">Documento</th>
                      <th scope="col" className="registry__col--optional">Categoria</th>
                      <th scope="col">Pagamento</th>
                    </tr>
                  </thead>
                  <tbody>
                    {!shown
                      ? Array.from({ length: 4 }, (_, i) => (
                          <tr key={i} className="registry__placeholder">
                            <td colSpan={4}>
                              <Skeleton width="100%" height="1.1rem" />
                            </td>
                          </tr>
                        ))
                      : items.length === 0
                        ? (
                          <tr className="biz-table__state">
                            <td colSpan={4}>{noSuppliersAtAll ? "Nenhum fornecedor cadastrado." : "Nenhum fornecedor encontrado com essa busca."}</td>
                          </tr>
                        )
                        : items.map((supplier) => (
                              <tr key={supplier.id}>
                                <td>
                                  <Link className="registry__name" to={`/dashboard/fornecedores/${supplier.id}`}>
                                    {supplier.legalName}
                                  </Link>
                                  {supplier.tradeName && <span className="registry__secondary">{supplier.tradeName}</span>}
                                </td>
                                <td className="registry__muted registry__col--optional">{supplier.documentMasked}</td>
                                <td className="registry__muted registry__col--optional">{supplier.category || "—"}</td>
                                <td className="registry__muted">{paymentMethodLabel(supplier)}</td>
                              </tr>
                            ))}
                  </tbody>
                </table>
              </div>
            )}

            {shown && totalCount > 0 && (
              <Pager
                label="Páginas de fornecedores"
                page={page}
                totalPages={totalPages}
                totalCount={totalCount}
                countLabel={totalCount === 1 ? "fornecedor" : "fornecedores"}
                onChange={setPage}
                loading={!loaded}
              />
            )}
          </section>

        <ModuleSection title="Pagamentos reconhecidos">
          <ModuleTable
            columns={[
              { label: "Data" },
              { label: "Fornecedor" },
              { label: "Descrição no extrato" },
              { label: "Conta" },
              { label: "Valor", numeric: true },
            ]}
          />
        </ModuleSection>
      </div>

      {formOpen && <SupplierFormDialog categories={summary?.categories ?? []} onClose={() => setFormOpen(false)} onSaved={handleSaved} />}
    </BusinessModule>
  );
}
