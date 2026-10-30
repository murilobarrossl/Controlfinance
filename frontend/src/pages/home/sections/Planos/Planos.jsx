import SectionHeading from "../../../../components/ui/SectionHeading/SectionHeading.jsx";
import { PLANS, FREE_TRIAL_DAYS, formatPrice, splitPrice } from "../../../../utils/pricing.js";
import "./Planos.css";

const { monthly, quarterly, annual } = PLANS;

const PLAN_CARDS = [
  {
    plan: monthly,
    billingNote: "cobrado mensalmente",
    benefits: [
      "Relatórios e dashboards.",
      "Planejamentos e metas.",
      "Sincronização bancária.",
      "Suporte a PF e PJ",
      "Gestão de receitas e despesas.",
    ],
  },
  {
    plan: quarterly,
    highlight: true,
    billingNote: `Cobrado ${formatPrice(quarterly.totalPrice)} a cada 3 meses`,
    benefitsTitle: "Todos os benefícios do mensal e mais:",
    benefits: [`${quarterly.discountPercent}% de economia`, "suporte prioritário"],
    extraNotes: [
      `Economia de ${formatPrice(quarterly.savingsPerPeriod)} a cada 3 meses em relação ao mensal`,
    ],
  },
  {
    plan: annual,
    billingNote: `cobrado ${formatPrice(annual.totalPrice)}/ano`,
    benefitsTitle: "Todos os benefícios do trimestral e mais:",
    benefits: [`${annual.discountPercent}% de economia`, "suporte prioritário com respostas mais rápidas"],
    extraNotes: [`Economia de ${formatPrice(annual.savingsPerPeriod)} por ano em relação ao mensal`],
  },
];

export default function Planos() {
  return (
    <section id="planos" className="planos">
      <SectionHeading kicker="Assinatura" title="Planos" />

      <div className="planos__grid">
        {PLAN_CARDS.map(({ plan, highlight, billingNote, benefitsTitle, benefits, extraNotes }) => {
          const price = splitPrice(plan.monthlyPrice);
          return (
            <div key={plan.id} className={`planos__card ${highlight ? "planos__card--highlight" : ""}`}>
              <div className="planos__card-head">
                <h3 className="planos__plan-name">{plan.name}</h3>
              </div>

              <p className="planos__price">
                {price.integer}
                <span className="planos__price-cents">{price.cents}</span>
                <span className="planos__price-period">/mês</span>
              </p>
              <p className="planos__billing-note">{billingNote}</p>

              {benefitsTitle && <p className="planos__benefits-title">{benefitsTitle}</p>}
              <ul className="planos__benefits">
                {benefits.map((benefit) => (
                  <li key={benefit}>
                    <span className="planos__check">✓</span> {benefit}
                  </li>
                ))}
              </ul>

              {extraNotes && (
                <ul className="planos__extra-notes">
                  {extraNotes.map((note) => (
                    <li key={note}>{note}</li>
                  ))}
                </ul>
              )}

              {/* Não existe checkout nem cobrança no sistema ainda: o botão não pode sugerir uma
                  contratação que não acontece (CDC arts. 30 e 37; Decreto 7.962/2013 art. 4º). */}
              <button className="planos__cta" type="button" disabled>
                Contratação em breve
              </button>
            </div>
          );
        })}
      </div>

      <p className="planos__footer-note">
        Preços de referência: a contratação paga ainda não está aberta e nenhuma cobrança é feita.
        Quando abrir, cada plano terá {FREE_TRIAL_DAYS} dias grátis, e as condições completas (renovação,
        cancelamento e reembolso) serão publicadas antes de qualquer cobrança.
      </p>
    </section>
  );
}
