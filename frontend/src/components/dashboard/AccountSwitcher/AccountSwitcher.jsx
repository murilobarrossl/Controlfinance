import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { ChevronDownIcon, CardIcon, MoreIcon } from "../../ui/icons/FeatureIcons.jsx";
import ConfirmDialog from "../../ui/ConfirmDialog/ConfirmDialog.jsx";
import { groupAccounts } from "../../../utils/accountGrouping.js";
import { isCreditCardAccount, getAccountKindLabel } from "../../../utils/accountKind.js";
import "./AccountSwitcher.css";

// Mesma cor usada na tarja ao lado de cada banco na tela de conectar-banco, nunca
// escolhida à mão aqui, só repassada do conector correspondente (via bankCode).
function resolveAccountColor(account, connectorsById) {
  const raw = connectorsById.get(account.bankCode)?.color;
  if (!raw) return "var(--color-border-strong)";
  return raw.startsWith("#") ? raw : `#${raw}`;
}

// Cartão de crédito não é "mais uma conta bancária": em vez da bolinha colorida das contas, ganha
// o ícone de cartão e a etiqueta do tipo, e dentro da conexão ele aparece recuado abaixo da conta
// corrente do mesmo banco, pra ficar claro que é um produto daquele banco e não um saldo a mais.
function AccountLabel({ account, connectorsById }) {
  const isCard = isCreditCardAccount(account);
  return (
    <>
      {isCard ? (
        <span className="account-switcher__card-icon" aria-hidden="true">
          <CardIcon />
        </span>
      ) : (
        <span
          className="account-switcher__dot"
          style={{ backgroundColor: resolveAccountColor(account, connectorsById) }}
        />
      )}
      <span className="account-switcher__name">{account.name}</span>
      <span className={`account-switcher__kind ${isCard ? "account-switcher__kind--card" : ""}`}>
        {getAccountKindLabel(account)}
      </span>
    </>
  );
}

// Conta corrente/poupança primeiro, cartões depois (recuados), dentro de cada conexão bancária.
function sortGroupAccounts(accounts) {
  return [...accounts].sort((a, b) => Number(isCreditCardAccount(a)) - Number(isCreditCardAccount(b)));
}

export default function AccountSwitcher({ accounts, activeAccountId, connectors, onSelect, onDisconnect, onRemoveManual }) {
  const [open, setOpen] = useState(false);
  const [actionsFor, setActionsFor] = useState(null);
  // { kind: "connection", id, label } | { kind: "manual", id, label } | null
  const [pendingRemoval, setPendingRemoval] = useState(null);
  const rootRef = useRef(null);

  useEffect(() => {
    if (!open) return undefined;
    function close() {
      setOpen(false);
      setActionsFor(null);
    }
    function handleClickOutside(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) close();
    }
    function handleKeyDown(e) {
      if (e.key === "Escape") close();
    }
    document.addEventListener("mousedown", handleClickOutside);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handleClickOutside);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [open]);

  if (accounts.length === 0) return null;

  const connectorsById = new Map(connectors.map((c) => [c.id, c]));
  const activeAccount = accounts.find((a) => a.id === activeAccountId) ?? accounts[0];
  const groups = groupAccounts(accounts, connectorsById);

  function closeMenu() {
    setOpen(false);
    setActionsFor(null);
  }

  return (
    <div className="account-switcher" ref={rootRef}>
      <button
        type="button"
        className="account-switcher__trigger"
        onClick={() => (open ? closeMenu() : setOpen(true))}
        aria-expanded={open}
      >
        <AccountLabel account={activeAccount} connectorsById={connectorsById} />
        <span className={`account-switcher__chevron ${open ? "account-switcher__chevron--open" : ""}`}>
          <ChevronDownIcon />
        </span>
      </button>

      {open && (
        <div className="account-switcher__menu" role="menu">
          {groups.map((group) => {
            const groupKey = group.accounts[0].id;
            const connectionId = group.accounts[0].localIntegrationId;
            const title = group.label ?? (connectionId ? group.accounts[0].name : null);
            const actionsOpen = actionsFor === groupKey;

            return (
              <div key={groupKey} className="account-switcher__group">
                {title && (
                  <div className="account-switcher__group-header">
                    <p className="account-switcher__group-label">{title}</p>
                    {connectionId && (
                      <div className="account-switcher__actions">
                        <button
                          type="button"
                          className="account-switcher__actions-trigger"
                          aria-haspopup="menu"
                          aria-expanded={actionsOpen}
                          aria-label={`Opções de ${title}`}
                          onClick={() => setActionsFor(actionsOpen ? null : groupKey)}
                        >
                          <MoreIcon />
                        </button>
                        {actionsOpen && (
                          <div className="account-switcher__actions-menu" role="menu">
                            <button
                              type="button"
                              role="menuitem"
                              className="account-switcher__actions-item"
                              onClick={() => {
                                setPendingRemoval({ kind: "connection", id: connectionId, label: title });
                                closeMenu();
                              }}
                            >
                              Desconectar banco
                            </button>
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )}

                {sortGroupAccounts(group.accounts).map((account) => {
                  const nestedClass =
                    isCreditCardAccount(account) && group.accounts.some((a) => !isCreditCardAccount(a))
                      ? "account-switcher__row--nested"
                      : "";
                  return account.id === activeAccount.id ? (
                    <div key={account.id} className={`account-switcher__current ${nestedClass}`}>
                      <AccountLabel account={account} connectorsById={connectorsById} />
                      {!account.localIntegrationId && (
                        <button
                          type="button"
                          className="account-switcher__disconnect"
                          onClick={() => {
                            setPendingRemoval({ kind: "manual", id: account.id, label: account.name });
                            closeMenu();
                          }}
                        >
                          Remover
                        </button>
                      )}
                    </div>
                  ) : (
                    <button
                      key={account.id}
                      type="button"
                      role="menuitem"
                      className={`account-switcher__option ${nestedClass}`}
                      onClick={() => {
                        onSelect(account.id);
                        closeMenu();
                      }}
                    >
                      <AccountLabel account={account} connectorsById={connectorsById} />
                    </button>
                  );
                })}

                <div className="account-switcher__divider" />
              </div>
            );
          })}

          <Link to="/conectar-banco" className="account-switcher__option account-switcher__option--add">
            + Conectar outro banco
          </Link>
        </div>
      )}

      <ConfirmDialog
        open={pendingRemoval !== null}
        title={pendingRemoval?.kind === "connection" ? `Desconectar ${pendingRemoval?.label}?` : `Remover ${pendingRemoval?.label}?`}
        message={
          pendingRemoval?.kind === "connection"
            ? "Todas as contas e cartões desta conexão saem do Control Finance, e apagamos na hora o saldo, o extrato e os parcelamentos que vieram desse banco. Também pedimos à Polp que exclua a conexão. Sua conta no Control Finance continua existindo. Para conectar de novo depois, é preciso autorizar outra vez no banco."
            : "A conta, os lançamentos e os parcelamentos dela serão apagados de forma definitiva."
        }
        confirmLabel={pendingRemoval?.kind === "connection" ? "Desconectar e apagar dados" : "Remover"}
        onConfirm={() => {
          const target = pendingRemoval;
          setPendingRemoval(null);
          if (target.kind === "connection") onDisconnect(target.id);
          else onRemoveManual(target.id);
        }}
        onClose={() => setPendingRemoval(null)}
      />
    </div>
  );
}
