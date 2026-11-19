import { transactionTone } from "../../../utils/financialTone.js";
import { resolveCategoryColor } from "../../../utils/categoryColors.js";
import { useEffect, useState } from "react";
import { Link, useLocation, useNavigate, useParams } from "react-router";
import { getTransaction } from "../../../api/transactions.js";
import { isAbortError } from "../../../api/client.js";
import MerchantLogo from "../../../components/ui/MerchantLogo/MerchantLogo.jsx";
import Skeleton from "../../../components/ui/Skeleton/Skeleton.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import usePageTitle from "../../../hooks/usePageTitle.js";
import "./TransactionDetail.css";

const ACCOUNT_OPERATIONS = {
  PIX: "Pix",
  TED: "TED",
  DOC: "DOC",
  TRANSFERENCIA_MESMA_INSTITUICAO: "Transferência na mesma instituição",
  BOLETO: "Boleto",
  CONVENIO_ARRECADACAO: "Pagamento de convênio",
  PACOTE_TARIFA_SERVICOS: "Pacote de tarifas",
  TARIFA_SERVICOS_AVULSOS: "Tarifa avulsa",
  FOLHA_PAGAMENTO: "Salário",
  DEPOSITO: "Depósito",
  SAQUE: "Saque",
  CARTAO: "Cartão",
  ENCARGOS_JUROS_CHEQUE_ESPECIAL: "Encargos do cheque especial",
  RENDIMENTO_APLIC_FINANCEIRA: "Rendimento de aplicação",
  PORTABILIDADE_SALARIO: "Portabilidade de salário",
  APLICACAO_FINANCEIRA: "Aplicação financeira",
  RESGATE_APLIC_FINANCEIRA: "Resgate de aplicação",
  OPERACAO_CREDITO: "Operação de crédito",
  TRANSFERENCIA_SALDO_RESERVADO: "Movimentação de saldo reservado",
  OUTROS: "Outros",
};

const DIRECTIONAL = {
  PIX: ["Pix recebido", "Pix enviado"],
  TED: ["TED recebida", "TED enviada"],
  DOC: ["DOC recebido", "DOC enviado"],
  TRANSFERENCIA_MESMA_INSTITUICAO: ["Transferência recebida", "Transferência enviada"],
};

const DETAILS_POLL_MS = 30000;

const PENDING_PARTY = {
  importing: "Carregando dados do banco",
  delayed: "Dados do banco indisponíveis no momento",
};

const BANK_NAMES = {
  "001": "Banco do Brasil",
  "033": "Santander",
  "041": "Banrisul",
  "070": "BRB",
  "077": "Banco Inter",
  "104": "Caixa Econômica Federal",
  "208": "BTG Pactual",
  "212": "Banco Original",
  "237": "Bradesco",
  "260": "Nubank",
  "290": "PagBank",
  "323": "Mercado Pago",
  "336": "C6 Bank",
  "341": "Itaú Unibanco",
  "380": "PicPay",
  "422": "Safra",
  "748": "Sicredi",
  "756": "Sicoob",
};

function bankLabel(code) {
  if (!code) return null;
  return BANK_NAMES[code.padStart(3, "0")] ?? null;
}

function formatDate(iso) {
  return new Date(iso).toLocaleDateString("pt-BR", { timeZone: "UTC", day: "2-digit", month: "long", year: "numeric" });
}

function formatDateTime(iso) {
  return new Date(iso).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
}

function formatTime(iso) {
  return new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}

function formatBillMonth(value) {
  const [year, month] = value.split("-").map(Number);
  const label = new Date(Date.UTC(year, month - 1, 1)).toLocaleDateString("pt-BR", { month: "long", year: "numeric", timeZone: "UTC" });
  return label.charAt(0).toUpperCase() + label.slice(1);
}

function formatForeign(amount, currency) {
  try {
    return new Intl.NumberFormat("pt-BR", { style: "currency", currency }).format(amount);
  } catch {
    return `${currency} ${amount}`;
  }
}

const TRANSFER_OPERATIONS = new Set(["PIX", "TED", "DOC", "TRANSFERENCIA_MESMA_INSTITUICAO"]);

const OTHER_PARTY_ROLE = {
  BOLETO: "Beneficiário",
  CONVENIO_ARRECADACAO: "Beneficiário",
  FOLHA_PAGAMENTO: "Fonte pagadora",
};

function formatCnpj(digits) {
  return digits.replace(/^(\d{2})(\d{3})(\d{3})(\d{4})(\d{2})$/, "$1.$2.$3/$4-$5");
}

const LOWERCASE_PARTICLES = new Set(["da", "das", "de", "do", "dos", "e"]);

// Extratos trazem nomes em caixa alta; só a apresentação muda, nunca o conteúdo.
function readableName(text) {
  if (!text || text !== text.toUpperCase() || !/^[\p{L} .'-]+$/u.test(text)) return text;
  return text
    .toLowerCase()
    .split(" ")
    .map((word, i) => (i > 0 && LOWERCASE_PARTICLES.has(word) ? word : word.charAt(0).toUpperCase() + word.slice(1)))
    .join(" ");
}

function joinUnique(parts) {
  const out = [];
  for (const part of parts) {
    if (!part) continue;
    const lower = part.toLowerCase();
    if (out.some((p) => p.toLowerCase().includes(lower) || lower.includes(p.toLowerCase()))) continue;
    out.push(part);
  }
  return out.join(" · ");
}

const MOVEMENT_TAGS = { CardBillPayment: "Pagamento de fatura", LoanDisbursement: "Empréstimo recebido" };

function movementTitle(t) {
  const income = t.type === "Income";
  if (t.movementKind === "CardBillPayment") return "Pagamento de fatura";
  if (t.movementKind === "LoanDisbursement") return "Empréstimo recebido";

  const code = t.bank.operationType;
  if (t.account.isCard) {
    if (code === "ESTORNO") return "Estorno no cartão";
    if (code === "CASHBACK") return "Cashback no cartão";
    if (code === "TARIFA") return "Tarifa do cartão";
    if (income) return "Crédito no cartão";
    return t.bank.installmentTotal ? "Compra parcelada no cartão" : "Compra no cartão";
  }

  if (code === "CARTAO") return "Compra no débito";
  if (DIRECTIONAL[code]) return DIRECTIONAL[code][income ? 0 : 1];
  if (ACCOUNT_OPERATIONS[code] && code !== "OUTROS") return ACCOUNT_OPERATIONS[code];
  return income ? "Entrada na conta" : "Saída da conta";
}

// O nome editável do lançamento nunca identifica pagador ou recebedor: só a contraparte do banco
// ou, sem ela, o nome lido da descrição original.
function counterparty(t) {
  const b = t.bank;
  if (b.counterpartyAlias || b.counterpartyLegalName) {
    const name = b.counterpartyAlias ?? b.counterpartyLegalName;
    const legal = b.counterpartyAlias && b.counterpartyLegalName !== b.counterpartyAlias ? b.counterpartyLegalName : null;
    return { name, legal, fromDescription: false };
  }
  if (b.describedParty) return { name: readableName(b.describedParty), legal: null, fromDescription: true };
  return null;
}

function ownSide(t) {
  const a = t.account;
  const meta = joinUnique([a.name, a.institutionName, a.number]);
  const usedCard = t.bank.cardNumber && t.bank.cardNumber !== a.number ? `Cartão usado ${t.bank.cardNumber}` : null;
  return { name: "Você", meta: [meta, usedCard].filter(Boolean) };
}

function otherSide(t, cp) {
  const b = t.bank;
  const own = t.movementKind === "OwnTransfer";
  const meta = [
    joinUnique([cp?.legal, b.counterpartyCnpj && `CNPJ ${formatCnpj(b.counterpartyCnpj)}`]),
    joinUnique([bankLabel(b.counterpartyBankCode), b.counterpartyAccount && `Conta ${b.counterpartyAccount}`]),
  ].filter(Boolean);
  if (cp) {
    const note = cp.fromDescription ? (t.account.isCard ? "Como aparece na fatura" : "Conforme a descrição do extrato") : null;
    return { name: cp.name, meta, note };
  }
  if (own) return { name: "Outra conta sua", meta };
  return { name: null, empty: PENDING_PARTY[b.detailsStatus] ?? "Nome não informado pelo banco", meta };
}

// Só mostra quem pagou e quem recebeu quando a operação tem outra parte (transferências) ou quando
// o banco a identificou. Saque, tarifa e rendimento não ganham um bloco vazio.
function participants(t, cp) {
  const code = t.bank.operationType;
  const pending = t.bank.detailsStatus !== "ready";
  const transfer = TRANSFER_OPERATIONS.has(code) || t.movementKind === "OwnTransfer";
  if (t.movementKind === "CardBillPayment") return null;
  if (!cp && !pending && !(transfer && !t.account.isCard)) return null;

  const income = t.type === "Income";
  const mine = ownSide(t);
  const other = otherSide(t, cp);
  if (t.account.isCard) {
    return income
      ? [{ role: "Estabelecimento", ...other }, { role: "Creditado em", ...mine }]
      : [{ role: "Pago com", ...mine }, { role: "Estabelecimento", ...other }];
  }
  const otherRole = transfer ? null : OTHER_PARTY_ROLE[code];
  return income
    ? [{ role: otherRole ?? "Quem pagou", ...other }, { role: "Quem recebeu", ...mine }]
    : [{ role: "Quem pagou", ...mine }, { role: otherRole ?? "Quem recebeu", ...other }];
}

function Row({ label, children }) {
  return (
    <div className="tx-detail__row">
      <dt className="tx-detail__label">{label}</dt>
      <dd className="tx-detail__value">{children}</dd>
    </div>
  );
}

function Participant({ role, name, empty, meta, note }) {
  return (
    <li className="tx-detail__party">
      <span className="tx-detail__party-role">{role}</span>
      {name ? <span className="tx-detail__party-name">{name}</span> : <span className="tx-detail__party-empty">{empty}</span>}
      {meta.map((line) => (
        <span key={line} className="tx-detail__party-meta">
          {line}
        </span>
      ))}
      {note && <span className="tx-detail__party-note">{note}</span>}
    </li>
  );
}

function DetailSkeleton() {
  return (
    <div className="tx-detail__body" aria-busy="true" aria-label="Carregando detalhes">
      <div className="tx-detail__header">
        <div className="tx-detail__identity">
          <Skeleton width="14rem" height="1.9rem" />
          <Skeleton width="16rem" height="1rem" />
        </div>
        <Skeleton width="9rem" height="2.2rem" />
      </div>
      <div className="tx-detail__columns">
        <div className="tx-detail__group">
          {Array.from({ length: 2 }, (_, i) => (
            <Skeleton key={i} width="70%" height="2.6rem" />
          ))}
        </div>
        <div className="tx-detail__group">
          {Array.from({ length: 4 }, (_, i) => (
            <Skeleton key={i} width="100%" height="1.4rem" />
          ))}
        </div>
      </div>
    </div>
  );
}

export default function TransactionDetail() {
  usePageTitle("Detalhes da transação");
  const { transactionId } = useParams();
  const navigate = useNavigate();
  const location = useLocation();
  const [state, setState] = useState({ id: null, data: null, error: null, notFound: false });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    getTransaction(transactionId, { signal: controller.signal })
      .then((data) => setState({ id: transactionId, data, error: null, notFound: false }))
      .catch((err) => {
        if (controller.signal.aborted || isAbortError(err)) return;
        setState({
          id: transactionId,
          data: null,
          notFound: err.status === 404 || err.status === 400,
          error: err.message || "Não foi possível carregar o lançamento.",
        });
      });
    return () => controller.abort();
  }, [transactionId, reloadKey]);

  // Só lê o banco de dados do Control Finance (nenhuma chamada ao banco do usuário) e para quando os detalhes chegam.
  const detailsStatus = state.id === transactionId ? state.data?.bank?.detailsStatus : null;
  useEffect(() => {
    if (!detailsStatus || detailsStatus === "ready") return;
    const controller = new AbortController();
    const timer = setInterval(() => {
      if (document.visibilityState !== "visible") return;
      getTransaction(transactionId, { signal: controller.signal })
        .then((data) => setState((prev) => (prev.id === transactionId ? { ...prev, data } : prev)))
        .catch(() => {});
    }, DETAILS_POLL_MS);
    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, [transactionId, detailsStatus]);

  function goBack() {
    if (location.state?.fromReport) navigate(-1);
    else navigate("/dashboard/relatorios");
  }

  const loading = state.id !== transactionId;
  const t = loading ? null : state.data;

  const back = (
    <button type="button" className="tx-detail__back" onClick={goBack}>
      <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <path d="M15 18l-6-6 6-6" />
      </svg>
      Relatórios
    </button>
  );

  if (loading) {
    return (
      <div className="tx-detail">
        {back}
        <DetailSkeleton />
      </div>
    );
  }

  if (!t) {
    return (
      <div className="tx-detail">
        {back}
        <div className="tx-detail__failure" role="alert">
          {state.notFound ? (
            <>
              <h1 className="tx-detail__failure-title">Lançamento não encontrado</h1>
              <p>Ele pode ter sido apagado, ou a conta dele foi desconectada.</p>
              <Link to="/dashboard/relatorios" className="tx-detail__action">Ir para Relatórios</Link>
            </>
          ) : (
            <>
              <p>{state.error}</p>
              <button type="button" className="tx-detail__action" onClick={() => setReloadKey((k) => k + 1)}>
                Tentar de novo
              </button>
            </>
          )}
        </div>
      </div>
    );
  }

  const income = t.type === "Income";
  const bank = t.bank;
  const isTransfer = t.movementKind !== "None";
  const future = t.status !== "Paid";
  const cp = counterparty(t);
  const parties = participants(t, cp);
  const displayName = t.name.includes("|") ? t.name.split("|").map((s) => s.trim()).filter(Boolean).join(" · ") : t.name;
  const heading = cp?.name ?? displayName;
  const customName = displayName !== heading && t.name !== bank.description ? displayName : null;
  const when = `${future ? "Previsto para " : ""}${formatDate(t.dueDate)}${bank.dateTime && !future ? `, ${formatTime(bank.dateTime)}` : ""}`;

  return (
    <div className="tx-detail">
      {back}

      <article className="tx-detail__body" aria-labelledby="tx-detail-title">
        <header className="tx-detail__header">
          <div className="tx-detail__identity">
            <h1 id="tx-detail-title" className="tx-detail__title">
              <MerchantLogo name={heading} isTransfer={isTransfer || TRANSFER_OPERATIONS.has(bank.operationType)} />
              <span>{heading}</span>
            </h1>
            <p className="tx-detail__subtitle">
              <span>{movementTitle(t)}</span>
              <span className="tx-detail__subtitle-separator" aria-hidden="true">·</span>
              <span>{when}</span>
            </p>
            {(future || isTransfer) && (
              <p className="tx-detail__tags">
                {future && <span className="tx-detail__tag">Lançamento futuro</span>}
                {isTransfer && (
                  <span className="tx-detail__tag">
                    {MOVEMENT_TAGS[t.movementKind] ?? "Entre suas contas"}, fora dos totais
                  </span>
                )}
              </p>
            )}
          </div>
          <p className={`tx-detail__amount tx-detail__amount--${transactionTone(t.type, t.amount, isTransfer)}`}>
            <span aria-hidden="true">{income ? "+" : "−"}</span>
            {formatCurrency(t.amount)}
            <span className="visually-hidden">{income ? " de entrada" : " de saída"}</span>
          </p>
        </header>

        <div className={`tx-detail__columns${parties ? "" : " tx-detail__columns--single"}`}>
          {parties && (
            <section className="tx-detail__group" aria-labelledby="tx-detail-parties">
              <h2 id="tx-detail-parties" className="tx-detail__group-title">
                {t.account.isCard ? "Compra" : "Origem e destino"}
              </h2>
              <ol className="tx-detail__parties">
                {parties.map((p) => (
                  <Participant key={p.role} {...p} />
                ))}
              </ol>
            </section>
          )}

          <section className="tx-detail__group" aria-labelledby="tx-detail-info">
            <h2 id="tx-detail-info" className="tx-detail__group-title">Detalhes</h2>
            <dl className="tx-detail__list">
              {!parties && (
                <Row label={t.account.isCard ? "Cartão" : "Conta"}>{joinUnique([t.account.name, t.account.institutionName, t.account.number])}</Row>
              )}
              <Row label="Categoria">
                <span className="tx-detail__category">
                  <span
                    className="tx-detail__category-dot"
                    style={{ backgroundColor: resolveCategoryColor(t.categoryName) }}
                    aria-hidden="true"
                  />
                  {t.categoryName || "Sem categoria"}
                </span>
              </Row>
              {bank.installmentTotal && (
                <Row label="Parcela">
                  {bank.installmentNumber} de {bank.installmentTotal}
                </Row>
              )}
              {bank.billMonth && <Row label="Fatura">{formatBillMonth(bank.billMonth)}</Row>}
              {bank.originalAmount != null && bank.originalCurrency && (
                <Row label="Valor na moeda da compra">{formatForeign(bank.originalAmount, bank.originalCurrency)}</Row>
              )}
              {bank.description && <Row label="No extrato">{bank.description}</Row>}
              {customName && <Row label="Seu nome para o lançamento">{customName}</Row>}
              {t.isFixed && <Row label="Gasto fixo">Sim</Row>}
              {t.account.lastSyncedAt && <Row label="Atualizado em">{formatDateTime(t.account.lastSyncedAt)}</Row>}
            </dl>
          </section>
        </div>

        <p className="tx-detail__disclaimer">Informações da transação. Não substitui o comprovante bancário.</p>
      </article>
    </div>
  );
}
