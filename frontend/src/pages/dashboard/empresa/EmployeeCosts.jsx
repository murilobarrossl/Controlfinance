import { useEffect, useState } from "react";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import FormDialog from "../../../components/ui/FormDialog/FormDialog.jsx";
import { MoneyField, TextField } from "../../../components/ui/FormDialog/FormField.jsx";
import { deleteEmployeeCost, getEmployeeObligations, saveEmployeeCost } from "../../../api/payroll.js";
import { isAbortError } from "../../../api/client.js";
import { formatCurrency } from "../../../utils/financeMath.js";
import { AddButton, Section, TextButton } from "./RecordDetailParts.jsx";
import useDialogSubmit from "./useDialogSubmit.js";

function CostDialog({ employeeId, cost, onClose, onSaved }) {
  const [name, setName] = useState(cost?.description ?? "");
  const [amount, setAmount] = useState(cost?.amount == null ? "" : String(cost.amount));
  const { submitting, error, setError, submit } = useDialogSubmit(
    () => saveEmployeeCost(employeeId, cost?.id, { name: name.trim(), amount: amount === "" ? null : Number(amount) }),
    onSaved,
  );

  return (
    <FormDialog
      open
      title={cost ? "Editar custo" : "Adicionar custo"}
      error={error}
      submitting={submitting}
      submitLabel="Salvar"
      cancelLabel={null}
      showCloseButton
      closeButtonPlacement="start"
      onClose={() => !submitting && onClose()}
      onSubmit={(e) => {
        e.preventDefault();
        if (name.trim().length < 2) return setError("Informe o título do custo.");
        submit(e);
      }}
    >
      <div className="form-dialog__grid">
        <TextField label="Título" example="Vale-transporte" maxLength={80} value={name} onChange={(e) => setName(e.target.value)} full required />
        <MoneyField label="Valor" value={amount} onChange={(e) => setAmount(e.target.value)} full />
      </div>
    </FormDialog>
  );
}

// Custos que a empresa decide registrar (título e valor livres). Nada é calculado; o total soma apenas os
// valores informados.
export default function EmployeeCosts({ employee }) {
  const [result, setResult] = useState(null);
  const [dialog, setDialog] = useState(null);
  const [confirming, setConfirming] = useState(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const data = result?.key === employee.id ? result.data : null;
  const loadError = result?.key === employee.id ? result.error : "";

  useEffect(() => {
    const controller = new AbortController();
    getEmployeeObligations(employee.id, { signal: controller.signal })
      .then((res) => setResult({ key: employee.id, data: res, error: "" }))
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setResult({ key: employee.id, data: null, error: err.message || "Não foi possível carregar os custos adicionais." });
      });
    return () => controller.abort();
  }, [employee.id]);

  function saved(res) {
    setResult({ key: employee.id, data: res, error: "" });
    setDialog(null);
  }

  async function remove(id) {
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      setResult({ key: employee.id, data: await deleteEmployeeCost(employee.id, id), error: "" });
      setConfirming(null);
    } catch (err) {
      setError(err.message || "Não foi possível excluir.");
    } finally {
      setBusy(false);
    }
  }

  let body;
  if (loadError) body = <p className="record-detail__error">{loadError}</p>;
  else if (!data) body = <Skeleton width="100%" height="5rem" />;
  else
    body = (
      <>
        {data.items.length === 0 ? (
          <p className="record-detail__empty">Nenhum custo cadastrado.</p>
        ) : (
          <ul className="cost-list">
            {data.items.map((cost) => (
              <li key={cost.id} className="cost">
                <span className="cost__name">{cost.description}</span>
                <div className="record-detail__method-actions cost__actions">
                  {confirming === cost.id ? (
                    <>
                      <TextButton tone="danger-strong" onClick={() => remove(cost.id)} disabled={busy}>
                        Confirmar exclusão
                      </TextButton>
                      <TextButton onClick={() => setConfirming(null)} disabled={busy}>
                        Cancelar
                      </TextButton>
                    </>
                  ) : (
                    <>
                      <TextButton onClick={() => setDialog({ cost })}>Editar</TextButton>
                      <TextButton
                        tone="danger"
                        onClick={() => {
                          setError("");
                          setConfirming(cost.id);
                        }}
                      >
                        Excluir
                      </TextButton>
                    </>
                  )}
                </div>
                <span className={cost.amount == null ? "cost__amount cost__amount--empty" : "cost__amount"}>
                  {cost.amount == null ? "Sem valor" : formatCurrency(cost.amount)}
                </span>
              </li>
            ))}
          </ul>
        )}
        {error && <p className="record-detail__error">{error}</p>}
        <div className="cost-total">
          <span>Total cadastrado</span>
          <strong>{formatCurrency(data.total)}</strong>
        </div>
      </>
    );

  return (
    <Section
      id="employee-costs"
      title="Custos adicionais"
      action={data && <AddButton onClick={() => setDialog({ cost: null })}>Adicionar custo</AddButton>}
    >
      {body}
      {dialog && <CostDialog employeeId={employee.id} cost={dialog.cost} onClose={() => setDialog(null)} onSaved={saved} />}
    </Section>
  );
}
