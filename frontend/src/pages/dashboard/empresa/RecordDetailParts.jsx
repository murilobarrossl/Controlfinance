import { payeeAccountTypeLabel, pixKeyTypeLabel } from "../../../utils/employees.js";

export function Field({ label, children, muted = false, tone = "default" }) {
  return (
    <div className="record-detail__field">
      <dt>{label}</dt>
      <dd className={muted ? "record-detail__value--muted" : `record-detail__value--${tone}`}>{children}</dd>
    </div>
  );
}

export function Section({ id, title, action, children }) {
  return (
    <section className="record-detail__section" aria-labelledby={id}>
      <div className="record-detail__section-head">
        <h2 id={id}>{title}</h2>
        {action && <div className="record-detail__section-actions">{action}</div>}
      </div>
      {children}
    </section>
  );
}

export function TextButton({ children, ...props }) {
  return (
    <button type="button" className="record-detail__text-button" {...props}>
      {children}
    </button>
  );
}

export function PaymentMethodsSection({ id, payee, onOpen }) {
  const { pix, bank } = payee;
  return (
    <Section
      id={id}
      title="Dados de pagamento"
      action={
        (!pix || !bank) && (
          <>
            {!pix && <TextButton onClick={() => onOpen("pix")}>Adicionar Pix</TextButton>}
            {!bank && <TextButton onClick={() => onOpen("bank")}>Adicionar conta</TextButton>}
          </>
        )
      }
    >
      {!pix && !bank ? (
        <p className="record-detail__empty">Nenhum dado de pagamento.</p>
      ) : (
        <ul className="record-detail__methods">
          {pix && (
            <li className="record-detail__method">
              <div>
                <span className="record-detail__method-label">Pix · {pixKeyTypeLabel(pix.type)}</span>
                <span className="record-detail__method-value">{pix.keyMasked}</span>
              </div>
              <div className="record-detail__method-actions">
                <TextButton onClick={() => onOpen("pix")}>Alterar</TextButton>
                <TextButton onClick={() => onOpen("removePix")}>Remover</TextButton>
              </div>
            </li>
          )}
          {bank && (
            <li className="record-detail__method">
              <div>
                <span className="record-detail__method-label">Transferência · {payeeAccountTypeLabel(bank.accountType)}</span>
                <span className="record-detail__method-value">
                  Banco {bank.bankCode} · Agência {bank.branch} · Conta {bank.accountMasked}
                </span>
              </div>
              <div className="record-detail__method-actions">
                <TextButton onClick={() => onOpen("bank")}>Alterar</TextButton>
                <TextButton onClick={() => onOpen("removeBank")}>Remover</TextButton>
              </div>
            </li>
          )}
        </ul>
      )}
    </Section>
  );
}
