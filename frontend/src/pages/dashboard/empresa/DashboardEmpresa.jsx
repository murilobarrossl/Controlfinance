import { useAuth } from "../../../context/AuthContext.jsx";
import DashboardHome from "../DashboardHome/DashboardHome.jsx";

// Ponto de entrada do dashboard empresarial. Por enquanto reaproveita o painel existente (mesmos dados
// reais, mesmo seletor de conta, mesmos cálculos), só com o nome da empresa no título. Indicadores
// próprios de gestão entram aqui, sem tocar no dashboard de pessoa física.
export default function DashboardEmpresa() {
  const { user } = useAuth();
  return <DashboardHome heading={user?.name || "Visão geral"} />;
}
