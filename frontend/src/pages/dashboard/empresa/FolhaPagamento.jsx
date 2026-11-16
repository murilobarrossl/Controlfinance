import { useState } from "react";
import { Link, useNavigate } from "react-router";
import BusinessModule from "./BusinessModule.jsx";
import EmployeeFormDialog from "./EmployeeFormDialog.jsx";
import Button from "../../../components/ui/Button/Button.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import StatusPill from "../../../components/ui/StatusPill/StatusPill.jsx";
import Pager from "../../../components/ui/Pager/Pager.jsx";
import { getEmployees, getEmployeesSummary } from "../../../api/employees.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import { transactionTone } from "../../../utils/financialTone.js";
import { EMPLOYEE_STATUS, formatDateOnly } from "../../../utils/employees.js";
import { StatusFilter, SummaryItem } from "./RegistryParts.jsx";
import useRegistryData, { useDebounced } from "./useRegistryData.js";
import "./RegistryList.css";

const PAGE_SIZE = 25;
const STATUS_FILTERS = [
  { value: "Active", label: "Ativos" },
  { value: "Terminated", label: "Desligados" },
  { value: "", label: "Todos" },
];

export default function FolhaPagamento() {
  const navigate = useNavigate();
  const [searchInput, setSearchInput] = useState("");
  const search = useDebounced(searchInput.trim(), 350);
  const [status, setStatus] = useState("Active");
  const [department, setDepartment] = useState("");
  const [refreshKey, setRefreshKey] = useState(0);
  const [formOpen, setFormOpen] = useState(false);
  const { summary, summaryError, page, setPage, loaded, listError, shown, refreshing, items, totalCount, totalPages } = useRegistryData({
    loadSummary: getEmployeesSummary,
    loadList: getEmployees,
    filters: { search, status, department },
    refreshKey,
    pageSize: PAGE_SIZE,
    listError: "Não foi possível carregar os funcionários.",
    summaryError: "Não foi possível carregar o resumo.",
  });
  const departments = summary?.departments ?? [];
  const noEmployeesAtAll = summary?.totalCount === 0;
  const hasRecords = summary?.totalCount > 0;

  function handleSaved(saved) {
    setFormOpen(false);
    setRefreshKey((k) => k + 1);
    navigate(`/dashboard/folha-de-pagamento/${saved.id}`);
  }

  return (
    <BusinessModule
      title="Folha de pagamento"
      ready
      actions={
        <Button as="button" type="button" variant="outline" size="sm" className="biz-module__register" onClick={() => setFormOpen(true)}>
          Cadastrar funcionário
        </Button>
      }
    >
      <div className="registry">

        {summaryError ? (
          <p className="registry__error">{summaryError}</p>
        ) : (
          <dl className="registry-summary summary-dividers" aria-busy={!summary}>
            <SummaryItem label="Funcionários ativos" value={summary ? summary.activeCount : <Skeleton width="2.5rem" height="1.6rem" />} />
            <SummaryItem
              label="Soma dos salários brutos (ativos)"
              tone={transactionTone("Expense", summary?.activeGrossSalaryTotal)}
              value={summary ? formatCurrency(summary.activeGrossSalaryTotal) : <Skeleton width="8rem" height="1.6rem" />}
            />
          </dl>
        )}

          <section className="registry__list" aria-labelledby="registry-list-title">
            <h2 id="registry-list-title" className="visually-hidden">
              Funcionários
            </h2>
            {hasRecords && (
              <div className="registry__toolbar">
                <input
                  type="search"
                  className="registry__search"
                  placeholder="Buscar por nome"
                  aria-label="Buscar funcionário pelo nome"
                  value={searchInput}
                  onChange={(e) => setSearchInput(e.target.value)}
                  autoComplete="off"
                />
                <StatusFilter options={STATUS_FILTERS} value={status} onChange={setStatus} />
                {departments.length > 0 && (
                  <label className="registry__select">
                    <span className="visually-hidden">Filtrar por departamento</span>
                    <select value={department} onChange={(e) => setDepartment(e.target.value)}>
                      <option value="">Todos os departamentos</option>
                      {departments.map((d) => (
                        <option key={d} value={d}>
                          {d}
                        </option>
                      ))}
                    </select>
                  </label>
                )}
              </div>
            )}

            {listError ? (
              <p className="registry__error">{listError}</p>
            ) : (
              <div className="registry__table-wrap">
                <table className={`biz-table registry__table${refreshing ? " registry__table--refreshing" : ""}`} aria-busy={!loaded}>
                  <thead>
                    <tr>
                      <th scope="col">Nome</th>
                      <th scope="col">Cargo</th>
                      <th scope="col" className="registry__col--optional">Departamento</th>
                      <th scope="col" className="registry__col--optional">Admissão</th>
                      <th scope="col" className="biz-table__numeric">Salário bruto</th>
                      <th scope="col">Situação</th>
                    </tr>
                  </thead>
                  <tbody>
                    {!shown
                      ? Array.from({ length: 4 }, (_, i) => (
                          <tr key={i} className="registry__placeholder">
                            <td colSpan={6}>
                              <Skeleton width="100%" height="1.1rem" />
                            </td>
                          </tr>
                        ))
                      : items.length === 0
                        ? (
                          <tr className="biz-table__state">
                            <td colSpan={6}>{noEmployeesAtAll ? "Nenhum funcionário cadastrado." : "Nenhum funcionário encontrado com esses filtros."}</td>
                          </tr>
                        )
                        : items.map((employee) => {
                            const statusInfo = EMPLOYEE_STATUS[employee.status] ?? EMPLOYEE_STATUS.Active;
                            return (
                              <tr key={employee.id}>
                                <td>
                                  <Link className="registry__name" to={`/dashboard/folha-de-pagamento/${employee.id}`}>
                                    {employee.fullName}
                                  </Link>
                                </td>
                                <td className="registry__muted">{employee.role}</td>
                                <td className="registry__muted registry__col--optional">{employee.department || "—"}</td>
                                <td className="registry__muted registry__col--optional">{formatDateOnly(employee.admissionDate)}</td>
                                <td className={`biz-table__numeric registry__amount--${transactionTone("Expense", employee.grossSalary)}`}>{formatCurrency(employee.grossSalary)}</td>
                                <td>
                                  <StatusPill status={statusInfo.pill}>{statusInfo.label}</StatusPill>
                                </td>
                              </tr>
                            );
                          })}
                  </tbody>
                </table>
              </div>
            )}

            {shown && totalCount > 0 && (
              <Pager
                label="Páginas de funcionários"
                page={page}
                totalPages={totalPages}
                totalCount={totalCount}
                countLabel={totalCount === 1 ? "funcionário" : "funcionários"}
                onChange={setPage}
                loading={!loaded}
              />
            )}
          </section>
      </div>

      {formOpen && (
        <EmployeeFormDialog onClose={() => setFormOpen(false)} onSaved={handleSaved} />
      )}
    </BusinessModule>
  );
}
