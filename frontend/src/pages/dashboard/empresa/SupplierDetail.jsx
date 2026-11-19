import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import Button from "../../../components/ui/Button/Button.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import ConfirmDialog from "../../../components/ui/ConfirmDialog/ConfirmDialog.jsx";
import SupplierFormDialog from "./SupplierFormDialog.jsx";
import { PaymentMethodDialog } from "./PaymentDialogs.jsx";
import { Field, PaymentMethodListSection, Section, TextButton } from "./RecordDetailParts.jsx";
import { paymentMethodRemoval } from "./payeePayment.js";
import {
  deleteSupplier,
  getSupplier,
  getSuppliersSummary,
  addSupplierPaymentMethod,
  removeSupplierPaymentMethod,
  revealSupplierPixKey,
  updateSupplierPaymentMethod,
} from "../../../api/suppliers.js";
import { isAbortError } from "../../../api/client.js";
import usePageTitle from "../../../hooks/usePageTitle.js";
import "./RecordDetail.css";

export default function SupplierDetail() {
  const { supplierId } = useParams();
  const navigate = useNavigate();
  const [result, setResult] = useState(null);
  const [categories, setCategories] = useState([]);
  const [dialog, setDialog] = useState(null);
  const [paymentTarget, setPaymentTarget] = useState(null);
  const [actionError, setActionError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const supplier = result?.key === supplierId ? result.data : null;
  usePageTitle(supplier ? supplier.legalName : "Fornecedor");

  useEffect(() => {
    const controller = new AbortController();
    getSupplier(supplierId, { signal: controller.signal })
      .then((data) => setResult({ key: supplierId, data, error: "" }))
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setResult({
          key: supplierId,
          data: null,
          error: err.status === 404 ? "Fornecedor não encontrado." : err.message || "Não foi possível carregar o fornecedor.",
        });
      });
    getSuppliersSummary({ signal: controller.signal })
      .then((data) => setCategories(data?.categories ?? []))
      .catch(() => {});
    return () => controller.abort();
  }, [supplierId]);

  function applySaved(saved) {
    setResult({ key: supplierId, data: saved, error: "" });
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
        navigate("/dashboard/fornecedores", { replace: true });
        return;
      }
      applySaved(saved);
    } catch (err) {
      setActionError(err.message || "Não foi possível concluir a ação.");
      setSubmitting(false);
    }
  }

  const back = (
    <Link to="/dashboard/fornecedores" className="record-detail__back">
      ‹ Fornecedores
    </Link>
  );

  if (result?.key === supplierId && result.error) {
    return (
      <div className="record-detail">
        {back}
        <p className="record-detail__error">{result.error}</p>
      </div>
    );
  }

  if (!supplier) {
    return (
      <div className="record-detail" aria-busy="true">
        {back}
        <Skeleton width="16rem" height="1.8rem" />
        <Skeleton width="100%" height="8rem" />
      </div>
    );
  }

  const confirmations = {
    ...(paymentTarget && {
      removePayment: paymentMethodRemoval(paymentTarget, () => removeSupplierPaymentMethod(supplier.id, paymentTarget.id)),
    }),
    remove: {
      title: `Excluir ${supplier.legalName}?`,
      message: "O cadastro e os dados de pagamento serão apagados de forma definitiva.",
      confirmLabel: "Excluir",
      run: () => deleteSupplier(supplier.id),
      deletes: true,
    },
  };
  const confirmation = confirmations[dialog];

  return (
    <div className="record-detail">
      {back}

      <header className="record-detail__header">
        <div className="record-detail__identity">
          <div className="record-detail__name-row">
            <h1>{supplier.legalName}</h1>
          </div>
          <p className="record-detail__meta">
            {[supplier.tradeName, `${supplier.documentType} ${supplier.documentMasked}`].filter(Boolean).join(" · ")}
          </p>
        </div>
        <Button as="button" type="button" variant="outline" size="sm" className="record-detail__danger" onClick={() => setDialog("remove")}>
          Excluir
        </Button>
      </header>

      <Section id="supplier-core" title="Dados cadastrais" action={<TextButton onClick={() => setDialog("core")}>Editar</TextButton>}>
        <dl className="record-detail__grid">
          <Field label="Nome ou razão social">{supplier.legalName}</Field>
          <Field label="Nome fantasia" muted={!supplier.tradeName}>
            {supplier.tradeName || "Não informado"}
          </Field>
          <Field label={supplier.documentType}>{supplier.documentMasked}</Field>
          <Field label="Categoria" muted={!supplier.category}>
            {supplier.category || "Não informada"}
          </Field>
        </dl>
      </Section>

      <PaymentMethodListSection
        id="supplier-payment"
        payee={supplier}
        loadPixKey={revealSupplierPixKey}
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

      {dialog === "core" && <SupplierFormDialog supplier={supplier} categories={categories} onClose={closeDialog} onSaved={applySaved} />}
      {dialog === "payment" && (
        <PaymentMethodDialog
          current={paymentTarget}
          save={(payload) =>
            paymentTarget
              ? updateSupplierPaymentMethod(supplier.id, paymentTarget.id, payload)
              : addSupplierPaymentMethod(supplier.id, payload)
          }
          onClose={closeDialog}
          onSaved={applySaved}
        />
      )}

      <ConfirmDialog
        confirmVariant="danger"
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
