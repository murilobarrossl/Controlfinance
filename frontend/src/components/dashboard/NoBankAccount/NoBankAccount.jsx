import { Link } from "react-router";
import Card from "../../ui/Card/Card.jsx";
import Button from "../../ui/Button/Button.jsx";
import { useAccount } from "../../../context/AccountContext.jsx";
import "./NoBankAccount.css";

export default function NoBankAccount({ variant = "accounts", pending = false }) {
  const { connections, connectionsStatus, bumpDataRefresh } = useAccount();
  const checking = connectionsStatus === "loading" && connections.length === 0;
  const failed = connectionsStatus === "error" && connections.length === 0;
  const unavailableAccount = variant === "accounts" && (pending || connections.length > 0);
  return (
    <Card className="no-bank-account">
      {checking ? <p role="status">Carregando conexões bancárias…</p>
        : failed ? <p role="alert">Não foi possível atualizar as conexões bancárias.</p>
        : unavailableAccount ? <p>A conexão bancária existe, mas ainda não há uma conta disponível nesta aba. Isso não significa ausência de cartões ou investimentos.</p>
        : variant === "investments" ? <p>Conecte uma instituição financeira para acompanhar seus investimentos e caixinhas.</p>
        : <p>
        Conecte uma conta bancária para ver saldo, receitas e despesas. Seus cartões continuam disponíveis na aba{" "}
        <Link to="/dashboard/cartoes">Cartões</Link>.
      </p>}
      {failed && <Button as="button" onClick={bumpDataRefresh} variant="outline" size="md">Tentar novamente</Button>}
      {!checking && !failed && !unavailableAccount && <Button as={Link} to="/conectar-banco" variant="primary" size="md">
        Conectar conta bancária
      </Button>}
    </Card>
  );
}
