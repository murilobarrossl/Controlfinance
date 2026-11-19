import { lazy, Suspense } from "react";
import { BrowserRouter, Navigate, Routes, Route, useLocation } from "react-router";
import { AuthProvider } from "./context/AuthContext.jsx";
import ProtectedRoute from "./routes/ProtectedRoute.jsx";
import { BusinessOnlyRoute, HolderSwitch, PersonalOnlyRoute } from "./routes/HolderRoute.jsx";
import usePolpConnectionWatcher from "./hooks/usePolpConnectionWatcher.js";
import HomePage from "./pages/home/HomePage.jsx";
import ErrorBoundary from "./components/ErrorBoundary/ErrorBoundary.jsx";
import ErrorPage, { PageLoading } from "./pages/errors/ErrorPage.jsx";

// Home and error recovery are immediately available; auth, dashboard and legal pages
// are split by route so public visitors do not download the dashboard.
const LoginPage = lazy(() => import("./pages/login/Login.jsx"));
const RegisterPage = lazy(() => import("./pages/cadastro/Cadastro.jsx"));
const ConnectBankPage = lazy(() => import("./pages/conectar-banco/ConectarBanco.jsx"));
const DashboardLayout = lazy(() => import("./layouts/DashboardLayout/DashboardLayout.jsx"));
const DashboardHome = lazy(() => import("./pages/dashboard/DashboardHome/DashboardHome.jsx"));
const Cartoes = lazy(() => import("./pages/dashboard/Cartoes/Cartoes.jsx"));
const ReceitasDespesas = lazy(() => import("./pages/dashboard/ReceitasDespesas/ReceitasDespesas.jsx"));
const CashMonth = lazy(() => import("./pages/dashboard/ReceitasDespesas/CashMonth.jsx"));
const Relatorios = lazy(() => import("./pages/dashboard/Relatorios/Relatorios.jsx"));
const TransactionDetail = lazy(() => import("./pages/dashboard/Relatorios/TransactionDetail.jsx"));
const Categorias = lazy(() => import("./pages/dashboard/Categorias/Categorias.jsx"));
const Investimentos = lazy(() => import("./pages/dashboard/Investimentos/Investimentos.jsx"));
const InvestmentDetail = lazy(() => import("./pages/dashboard/Investimentos/InvestmentDetail.jsx"));
const DashboardEmpresa = lazy(() => import("./pages/dashboard/empresa/DashboardEmpresa.jsx"));
const FolhaPagamento = lazy(() => import("./pages/dashboard/empresa/FolhaPagamento.jsx"));
const EmployeeDetail = lazy(() => import("./pages/dashboard/empresa/EmployeeDetail.jsx"));
const DespesasRecorrentes = lazy(() => import("./pages/dashboard/empresa/DespesasRecorrentes.jsx"));
const RecurringExpenseDetail = lazy(() => import("./pages/dashboard/empresa/RecurringExpenseDetail.jsx"));
const Fornecedores = lazy(() => import("./pages/dashboard/empresa/Fornecedores.jsx"));
const Impostos = lazy(() => import("./pages/dashboard/empresa/Impostos.jsx"));
const SupplierDetail = lazy(() => import("./pages/dashboard/empresa/SupplierDetail.jsx"));
const Privacidade = lazy(() => import("./pages/dashboard/Privacidade/Privacidade.jsx"));
const TermosDeUso = lazy(() => import("./pages/legal/TermosDeUso.jsx"));
const PoliticaDePrivacidade = lazy(() => import("./pages/legal/PoliticaDePrivacidade.jsx"));

function PolpConnectionWatcher() {
  usePolpConnectionWatcher();
  return null;
}

function AppRoutes() {
  const { pathname, search } = useLocation();
  return (
    <ErrorBoundary resetKey={`${pathname}${search}`}>
      <PolpConnectionWatcher />
      <Suspense fallback={<PageLoading />}>
        <Routes>
          <Route path="/" element={<HomePage />} />
          <Route path="/loginemail" element={<LoginPage mode="email" />} />
          <Route path="/logincpf-cnpj" element={<LoginPage mode="cpf" />} />
          <Route path="/register" element={<RegisterPage />} />
          <Route
            path="/conectar-banco"
            element={
              <ProtectedRoute>
                <ConnectBankPage />
              </ProtectedRoute>
            }
          />
          <Route path="/termos-de-uso" element={<TermosDeUso />} />
          <Route path="/politica-de-privacidade" element={<PoliticaDePrivacidade />} />
          <Route
            path="/dashboard"
            element={
              <ProtectedRoute>
                <DashboardLayout />
              </ProtectedRoute>
            }
          >
            <Route index element={<HolderSwitch personal={<DashboardHome />} business={<DashboardEmpresa />} />} />
            <Route path="cartoes" element={<Cartoes />} />
            <Route
              path="receitas-despesas"
              element={<HolderSwitch personal={<ReceitasDespesas />} business={<Navigate to="/dashboard/fluxo-de-caixa" replace />} />}
            />
            <Route path="relatorios" element={<Relatorios />} />
            <Route path="relatorios/:transactionId" element={<TransactionDetail />} />
            <Route path="categorias" element={<PersonalOnlyRoute><Categorias /></PersonalOnlyRoute>} />
            <Route path="investimentos" element={<PersonalOnlyRoute><Investimentos /></PersonalOnlyRoute>} />
            <Route path="investimentos/:investmentId" element={<PersonalOnlyRoute><InvestmentDetail /></PersonalOnlyRoute>} />
            <Route path="investimentos/caixinhas/:reserveId" element={<PersonalOnlyRoute><InvestmentDetail /></PersonalOnlyRoute>} />
            <Route path="privacidade" element={<Privacidade />} />
            <Route path="folha-de-pagamento" element={<BusinessOnlyRoute><FolhaPagamento /></BusinessOnlyRoute>} />
            <Route path="folha-de-pagamento/:employeeId" element={<BusinessOnlyRoute><EmployeeDetail /></BusinessOnlyRoute>} />
            <Route path="despesas-recorrentes" element={<DespesasRecorrentes />} />
            <Route path="despesas-recorrentes/:recurringId" element={<RecurringExpenseDetail />} />
            <Route path="fornecedores" element={<BusinessOnlyRoute><Fornecedores /></BusinessOnlyRoute>} />
            <Route path="fornecedores/:supplierId" element={<BusinessOnlyRoute><SupplierDetail /></BusinessOnlyRoute>} />
            <Route path="impostos" element={<BusinessOnlyRoute><Impostos /></BusinessOnlyRoute>} />
            <Route path="fluxo-de-caixa" element={<BusinessOnlyRoute><ReceitasDespesas /></BusinessOnlyRoute>} />
            <Route path="fluxo-de-caixa/mes/:month" element={<BusinessOnlyRoute><CashMonth /></BusinessOnlyRoute>} />
            <Route path="*" element={<ErrorPage code={404} embedded />} />
          </Route>
          <Route path="/403" element={<ErrorPage code={403} />} />
          <Route path="/404" element={<ErrorPage code={404} />} />
          <Route path="/500" element={<ErrorPage code={500} />} />
          <Route path="/503" element={<ErrorPage code={503} />} />
          <Route path="*" element={<ErrorPage code={404} />} />
        </Routes>
      </Suspense>
    </ErrorBoundary>
  );
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </AuthProvider>
  );
}
