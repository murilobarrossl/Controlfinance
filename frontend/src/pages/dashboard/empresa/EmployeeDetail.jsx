import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import Button from "../../../components/ui/Button/Button.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import FormDialog from "../../../components/ui/FormDialog/FormDialog.jsx";
import { DateField } from "../../../components/ui/FormDialog/FormField.jsx";
import EmployeeFormDialog from "./EmployeeFormDialog.jsx";
import { ClassificationDialog } from "./EmployeeDetailDialogs.jsx";
import { PaymentMethodDialog } from "./PaymentDialogs.jsx";
import { Field, PaymentMethodListSection, PaymentMonthToggle, Section, TextButton, usePaymentMonth } from "./RecordDetailParts.jsx";
import EmployeeCosts from "./EmployeeCosts.jsx";
import { paymentMethodRemoval } from "./payeePayment.js";
import {
  deleteEmployee,
  getEmployee,
  getEmployeePaymentMonth,
  getEmployeesSummary,
  reactivateEmployee,
  addEmployeePaymentMethod,
  removeEmployeePaymentMethod,
  revealEmployeePixKey,
  setEmployeePaymentMonth,
  updateEmployeePaymentMethod,
  terminateEmployee,
} from "../../../api/employees.js";
import { isAbortError } from "../../../api/client.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import { employmentTypeLabel, formatDateOnly, todayIso } from "../../../utils/employees.js";
import usePageTitle from "../../../hooks/usePageTitle.js";
import "./RecordDetail.css";

const CONFIRMATIONS = {
  reactivate: (e) => ({
    title: `Reativar ${e.fullName}?`,
    message: "Volta para a lista de ativos e para a soma dos salários.",
    confirmLabel: "Reativar",
    run: () => reactivateEmployee(e.id),
  }),
  remove: (e) => ({
    title: `Excluir ${e.fullName}?`,
    message: "O cadastro, o salário e os dados de pagamento serão apagados de forma definitiva.",
    confirmLabel: "Excluir",
    run: () => deleteEmployee(e.id),
    deletes: true,
  }),
};

export default function EmployeeDetail() {
  const { employeeId } = useParams();
  const navigate = useNavigate();
  const [result, setResult] = useState(null);
  const [departments, setDepartments] = useState([]);
  const [dialog, setDialog] = useState(null);
  const [paymentTarget, setPaymentTarget] = useState(null);
  const [terminationDate, setTerminationDate] = useState(todayIso);
  const [actionError, setActionError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const employee = result?.key === employeeId ? result.data : null;
  usePageTitle(employee ? employee.fullName : "Funcionário");
  const paymentMonth = usePaymentMonth(employeeId, getEmployeePaymentMonth, setEmployeePaymentMonth);

  useEffect(() => {
    const controller = new AbortController();
    getEmployee(employeeId, { signal: controller.signal })
      .then((data) => setResult({ key: employeeId, data, error: "" }))
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setResult({
          key: employeeId,
          data: null,
          error: err.status === 404 ? "Funcionário não encontrado." : err.message || "Não foi possível carregar o funcionário.",
        });
      });
    getEmployeesSummary({ signal: controller.signal })
      .then((data) => setDepartments(data?.departments ?? []))
      .catch(() => {});
    return () => controller.abort();
  }, [employeeId]);

  function applySaved(saved) {
    setResult({ key: employeeId, data: saved, error: "" });
    setDialog(null);
    setActionError("");
    setSubmitting(false);
  }

  function closeDialog() {
    if (submitting) return;
    setDialog(null);
    setActionError("");
  }

  async function runAction(action, { deletes = false } = {}) {
    if (submitting) return;
    setSubmitting(true);
    setActionError("");
    try {
      const saved = await action();
      if (deletes) {
        navigate("/dashboard/folha-de-pagamento", { replace: true });
        return;
      }
      applySaved(saved);
    } catch (err) {
      setActionError(err.message || "Não foi possível concluir a ação.");
      setSubmitting(false);
    }
  }

  const back = (
    <Link to="/dashboard/folha-de-pagamento" className="record-detail__back">
      ‹ Folha de pagamento
    </Link>
  );

  if (result?.key === employeeId && result.error) {
    return (
      <div className="record-detail">
        {back}
        <p className="record-detail__error">{result.error}</p>
      </div>
    );
  }

  if (!employee) {
    return (
      <div className="record-detail" aria-busy="true">
        {back}
        <Skeleton width="16rem" height="1.8rem" />
        <Skeleton width="100%" height="8rem" />
      </div>
    );
  }

  const isActive = employee.status === "Active";
  const confirmations = {
    ...(paymentTarget && {
      removePayment: paymentMethodRemoval(paymentTarget, () => removeEmployeePaymentMethod(employee.id, paymentTarget.id)),
    }),
    reactivate: CONFIRMATIONS.reactivate(employee),
    remove: CONFIRMATIONS.remove(employee),
  };
  const confirmation = confirmations[dialog];

  return (
    <div className="record-detail">
      {back}

      <header className="record-detail__header">
        <div className="record-detail__identity">
          <div className="record-detail__name-row">
            <h1>{employee.fullName}</h1>
          </div>
          <p className="record-detail__meta">
            {employee.role} · Admissão em {formatDateOnly(employee.admissionDate)}
            {!isActive && ` · Desligado em ${formatDateOnly(employee.terminationDate)}`}
          </p>
        </div>
        {isActive ? (
          <Button
            as="button"
            type="button"
            variant="outline"
            size="sm"
            onClick={() => {
              setTerminationDate(todayIso());
              setDialog("terminate");
            }}
          >
            Desligar
          </Button>
        ) : (
          <div className="record-detail__header-actions">
            <Button as="button" type="button" variant="outline" size="sm" onClick={() => setDialog("reactivate")}>
              Reativar
            </Button>
            <Button as="button" type="button" variant="outline" size="sm" className="record-detail__danger" onClick={() => setDialog("remove")}>
              Excluir
            </Button>
          </div>
        )}
      </header>

      <Section id="employee-core" title="Dados principais" action={<TextButton onClick={() => setDialog("core")}>Editar</TextButton>}>
        <dl className="record-detail__grid">
          <Field label="CPF">{employee.cpfMasked}</Field>
          <Field label="Cargo ou função">{employee.role}</Field>
          <Field label="Admissão">{formatDateOnly(employee.admissionDate)}</Field>
          <Field label="Salário bruto mensal" tone="money">
            {formatCurrency(employee.grossSalary)}
          </Field>
          <Field
            label="Pago no mês (identificado pelo banco)"
            tone="money"
            muted={paymentMonth.loaded && paymentMonth.state?.bankPaidAmount == null}
          >
            {!paymentMonth.loaded
              ? <Skeleton width="6rem" />
              : !paymentMonth.state
              ? "Indisponível"
              : paymentMonth.state.bankPaidAmount != null
              ? formatCurrency(paymentMonth.state.bankPaidAmount)
              : "Nenhum pagamento identificado"}
          </Field>
        </dl>
      </Section>

      <Section
        id="employee-classification"
        title="Vínculo e departamento"
        action={<TextButton onClick={() => setDialog("classification")}>Editar</TextButton>}
      >
        <dl className="record-detail__grid">
          <Field label="Tipo de vínculo" muted={!employee.employmentType}>
            {employee.employmentType ? employmentTypeLabel(employee.employmentType) : "Não informado"}
          </Field>
          <Field label="Departamento" muted={!employee.department}>
            {employee.department || "Não informado"}
          </Field>
        </dl>
      </Section>

      <PaymentMethodListSection
        id="employee-payment"
        payee={employee}
        loadPixKey={revealEmployeePixKey}
        onAdd={() => {
          setPaymentTarget(null);
          setDialog("payment");
        }}
        onEdit={(method) => {
          setPaymentTarget(method);
          setDialog("payment");
        }}
        onRemove={(method) => {
          setPaymentTarget(method);
          setDialog("removePayment");
        }}
      />

      <EmployeeCosts employee={employee} />

      <PaymentMonthToggle key={employee.id} month={paymentMonth} />

      {dialog === "core" && <EmployeeFormDialog employee={employee} onClose={closeDialog} onSaved={applySaved} />}
      {dialog === "classification" && (
        <ClassificationDialog employee={employee} departments={departments} onClose={closeDialog} onSaved={applySaved} />
      )}
      {dialog === "payment" && (
        <PaymentMethodDialog
          current={paymentTarget}
          save={(payload) =>
            paymentTarget
              ? updateEmployeePaymentMethod(employee.id, paymentTarget.id, payload)
              : addEmployeePaymentMethod(employee.id, payload)
          }
          onClose={closeDialog}
          onSaved={applySaved}
        />
      )}

      <FormDialog
        open={dialog === "terminate"}
        title={`Desligar ${employee.fullName}`}
        description="O cadastro continua no histórico e pode ser reativado."
        error={actionError}
        submitLabel="Desligar"
        submitting={submitting}
        onClose={closeDialog}
        onSubmit={(e) => {
          e.preventDefault();
          runAction(() => terminateEmployee(employee.id, terminationDate));
        }}
      >
        <DateField label="Data de desligamento" value={terminationDate} min={employee.admissionDate} onChange={setTerminationDate} required />
      </FormDialog>

      <ConfirmDialog
        confirmVariant={dialog === "reactivate" ? "primary" : "danger"}
        open={Boolean(confirmation)}
        title={confirmation?.title}
        message={actionError || confirmation?.message}
        confirmLabel={submitting ? "Aguarde..." : confirmation?.confirmLabel}
        onConfirm={() => confirmation && runAction(confirmation.run, { deletes: confirmation.deletes })}
        onClose={closeDialog}
      />
    </div>
  );
}
