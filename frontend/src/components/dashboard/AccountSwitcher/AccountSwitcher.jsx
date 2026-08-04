import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { ChevronDownIcon } from "../../ui/icons/FeatureIcons.jsx";
import "./AccountSwitcher.css";

// Mesma cor usada na tarja ao lado de cada banco na tela de conectar-banco, nunca
// escolhida à mão aqui, só repassada do conector correspondente (via bankCode).
function resolveAccountColor(account, connectorsById) {
  const raw = connectorsById.get(account.bankCode)?.color;
  if (!raw) return "var(--color-border-strong)";
  return raw.startsWith("#") ? raw : `#${raw}`;
}

// Contas com o mesmo polpIntegrationId vieram da mesma conexão bancária (ex.: conta corrente +
// cartão de crédito do mesmo banco, sincronizados juntos) — agrupa só pra deixar isso visualmente
// claro no seletor. Cada conta continua com sua própria visão de dados ao ser selecionada, nada é
// somado entre elas; contas sem polpIntegrationId (criadas manualmente) formam grupo de uma só.
function groupAccounts(accounts, connectorsById) {
  const groups = new Map();
  for (const account of accounts) {
    const key = account.polpIntegrationId ?? `single-${account.id}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(account);
  }
  return [...groups.values()].map((group) => ({
    accounts: group,
    label: connectorsById.get(group[0].bankCode)?.name,
  }));
}

export default function AccountSwitcher({ accounts, activeAccountId, connectors, onSelect, onDisconnect }) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef(null);

  useEffect(() => {
    function handleClickOutside(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false);
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  if (accounts.length === 0) return null;

  const connectorsById = new Map(connectors.map((c) => [c.id, c]));
  const activeAccount = accounts.find((a) => a.id === activeAccountId) ?? accounts[0];
  const groups = groupAccounts(accounts, connectorsById);

  return (
    <div className="account-switcher" ref={rootRef}>
      <button
        type="button"
        className="account-switcher__trigger"
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
      >
        <span
          className="account-switcher__dot"
          style={{ backgroundColor: resolveAccountColor(activeAccount, connectorsById) }}
        />
        <span className="account-switcher__name">{activeAccount.name}</span>
        <span className={`account-switcher__chevron ${open ? "account-switcher__chevron--open" : ""}`}>
          <ChevronDownIcon />
        </span>
      </button>

      {open && (
        <div className="account-switcher__menu" role="menu">
          {groups.map((group) => (
            <div key={group.accounts[0].id}>
              {group.accounts.length > 1 && (
                <p className="account-switcher__group-label">
                  {group.label ?? "Mesma conexão bancária"} · {group.accounts.length} contas vinculadas
                </p>
              )}

              {group.accounts.map((account) =>
                account.id === activeAccount.id ? (
                  <div key={account.id} className="account-switcher__current">
                    <span
                      className="account-switcher__dot"
                      style={{ backgroundColor: resolveAccountColor(account, connectorsById) }}
                    />
                    <span className="account-switcher__name">{account.name}</span>
                    <button
                      type="button"
                      className="account-switcher__disconnect"
                      onClick={() => {
                        onDisconnect(account.id);
                        setOpen(false);
                      }}
                    >
                      Sair
                    </button>
                  </div>
                ) : (
                  <button
                    key={account.id}
                    type="button"
                    role="menuitem"
                    className="account-switcher__option"
                    onClick={() => {
                      onSelect(account.id);
                      setOpen(false);
                    }}
                  >
                    <span
                      className="account-switcher__dot"
                      style={{ backgroundColor: resolveAccountColor(account, connectorsById) }}
                    />
                    <span className="account-switcher__name">{account.name}</span>
                  </button>
                )
              )}

              <div className="account-switcher__divider" />
            </div>
          ))}

          <Link to="/conectar-banco" className="account-switcher__option account-switcher__option--add">
            + Conectar outro banco
          </Link>
        </div>
      )}
    </div>
  );
}
