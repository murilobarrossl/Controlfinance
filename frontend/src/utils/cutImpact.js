// Calcula o efeito de cortar um percentual do gasto de uma categoria, cruzando com a reserva de
// emergência e a meta mais próxima de ser concluída.
//
// Fonte de metas/reserva, por enquanto: localStorage (getGoals/getReserve, ver
// investmentStorage.js e emergencyReserveStorage.js). Dado só do navegador, não acompanha o
// usuário entre dispositivos. Esta função só recebe os valores já carregados (reserveAmount,
// goals), então quando essa fonte migrar pro backend (metas/reserva por usuário no Postgres),
// só o carregamento na tela muda; a lógica de impacto abaixo continua igual.
export function calculateCutImpact({ categoryAmount, cutRate, reserveAmount, goals }) {
  const cutAmount = categoryAmount * cutRate;

  const reserveImpact = {
    current: reserveAmount,
    afterCut: reserveAmount + cutAmount,
  };

  // Entre as metas ainda não concluídas, a que está com menor valor faltando é a que o corte
  // deixa visivelmente mais perto de bater, mais motivador do que diluir o valor numa meta
  // distante.
  const openGoals = (goals || []).filter((g) => g.currentAmount < g.targetAmount);
  let goalImpact = null;
  if (openGoals.length > 0) {
    const nearest = [...openGoals].sort(
      (a, b) => a.targetAmount - a.currentAmount - (b.targetAmount - b.currentAmount)
    )[0];
    const remainingBefore = nearest.targetAmount - nearest.currentAmount;
    goalImpact = {
      name: nearest.name,
      remainingBefore,
      remainingAfter: Math.max(0, remainingBefore - cutAmount),
    };
  }

  return { cutAmount, remainingAmount: categoryAmount - cutAmount, reserveImpact, goalImpact };
}
