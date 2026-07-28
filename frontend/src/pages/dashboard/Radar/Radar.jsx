import { useEffect, useMemo, useState } from "react";
import { getRecurrences, setRecurrenceDecision } from "../../../api/recurrences.js";
import Card from "../../../components/ui/Card/Card.jsx";
import SectionHeading from "../../../components/ui/SectionHeading/SectionHeading.jsx";
import RecurrenceCard from "../../../components/dashboard/RecurrenceCard/RecurrenceCard.jsx";
import { formatCurrency } from "../../../utils/financeMath.js";
import "./Radar.css";

// "Mixed" só aparece pra quem realmente tem uma conta marcada como mista (ver BankAccount.Ownership
// no backend) — a maioria dos usuários (só PF ou só PJ) nunca vê essa seção.
const SECTIONS = [
  { key: "Personal", title: "Pessoais" },
  { key: "Business", title: "Empresa" },
  { key: "Mixed", title: "Contas mistas" },
];

export default function Radar() {
  const [recurrences, setRecurrences] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [actionError, setActionError] = useState("");

  useEffect(() => {
    getRecurrences()
      .then(setRecurrences)
      .catch((err) => setError(err.message || "Não foi possível carregar o radar."))
      .finally(() => setLoading(false));
  }, []);

  // Sempre reconsulta o backend depois de uma decisão em vez de atualizar o item localmente: a
  // resposta de "o que aparece agora" depende de regras (dispensar remove, confirmar pode puxar
  // uma recorrência que só tinha 1 ocorrência) que já existem no servidor — reimplementar isso no
  // cliente só pra economizar uma requisição arriscaria os dois lados divergirem.
  async function handleDecision(recurrence, updates) {
    setActionError("");
    try {
      await setRecurrenceDecision({
        bankAccountId: recurrence.bankAccountId,
        name: recurrence.displayName,
        ...updates,
      });
      setRecurrences(await getRecurrences());
    } catch (err) {
      setActionError(err.message || "Não foi possível salvar sua decisão.");
    }
  }

  const totalAnnual = useMemo(() => recurrences.reduce((sum, r) => sum + r.annualCost, 0), [recurrences]);

  if (loading) return <p className="radar__hint">Carregando...</p>;
  if (error) return <p className="radar__error">{error}</p>;

  return (
    <div className="radar">
      <SectionHeading kicker="Assinaturas e cobranças recorrentes" title="Radar de Recorrências" align="left" />

      {recurrences.length === 0 ? (
        <p className="radar__hint">Nenhuma recorrência detectada ainda.</p>
      ) : (
        <>
          <p className="radar__summary">
            Você tem {recurrences.length} recorrência{recurrences.length === 1 ? "" : "s"} ativa
            {recurrences.length === 1 ? "" : "s"}, somando <strong>{formatCurrency(totalAnnual)}/ano</strong>.
          </p>

          {actionError && <p className="radar__error">{actionError}</p>}

          {SECTIONS.map(({ key, title }) => {
            const items = recurrences.filter((r) => r.accountOwnership === key);
            if (items.length === 0) return null;

            return (
              <Card key={key} title={title}>
                <ul className="radar__list">
                  {items.map((r) => (
                    <RecurrenceCard
                      key={`${r.bankAccountId}-${r.normalizedName}`}
                      recurrence={r}
                      onDecision={(updates) => handleDecision(r, updates)}
                    />
                  ))}
                </ul>
              </Card>
            );
          })}
        </>
      )}
    </div>
  );
}
