import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { ChevronDownIcon, CardIcon } from "../../ui/icons/FeatureIcons.jsx";
import { syncIntegration } from "../../../api/polp.js";
import ConfirmDialog from "../../ui/ConfirmDialog/ConfirmDialog.jsx";
import { groupAccounts } from "../../../utils/accountGrouping.js";
import { isCreditCardAccount, getAccountKindLabel } from "../../../utils/accountKind.js";
import "./AccountSwitcher.css";

// Some tempo suficiente pro usuário ler o resultado ("Sincronização iniciada" ou o erro) antes
// do botão voltar sozinho pro estado normal, sem precisar de um clique extra pra limpar o feedback.
const SYNC_FEEDBACK_DURATION_MS = 4000;

// O sync roda em background no servidor (a Polp já levou até 20s por chamada nos logs). O
// clique só confirma que foi enfileirado, não que já terminou. Espera um tempo plausível antes
// de recarregar os dados do dashboard, em vez de recarregar na hora e mostrar o estado antigo
// como se já fosse o resultado da sincronização.
const SYNC_REFRESH_DELAY_MS = 8000;

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

export default function AccountSwitcher({ accounts, activeAccountId, connectors, onSelect, onDisconnect, onRemoveManual, onSynced }) {
  const [open, setOpen] = useState(false);
  // { kind: "connection", id, label } | { kind: "manual", id, label } | null
  const [pendingRemoval, setPendingRemoval] = useState(null);
  const [syncState, setSyncState] = useState({}); // localIntegrationId -> { status: "loading" | "success" | "error", message? }
  const rootRef = useRef(null);

  useEffect(() => {
    function handleClickOutside(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false);
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  async function handleSync(localIntegrationId) {
    setSyncState((prev) => ({ ...prev, [localIntegrationId]: { status: "loading" } }));
    try {
      await syncIntegration(localIntegrationId);
      setSyncState((prev) => ({ ...prev, [localIntegrationId]: { status: "queued" } }));
      setTimeout(() => onSynced?.(), SYNC_REFRESH_DELAY_MS);
    } catch (err) {
      setSyncState((prev) => ({ ...prev, [localIntegrationId]: { status: "error", message: err.message } }));
    } finally {
      setTimeout(() => {
        setSyncState((prev) => {
          const { [localIntegrationId]: _removed, ...rest } = prev;
          return rest;
        });
      }, SYNC_FEEDBACK_DURATION_MS);
    }
  }

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
        <AccountLabel account={activeAccount} connectorsById={connectorsById} />
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
                          setOpen(false);
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
                      setOpen(false);
                    }}
                  >
                    <AccountLabel account={account} connectorsById={connectorsById} />
                  </button>
                );
              })}

              {group.accounts[0].localIntegrationId && (
                <div className="account-switcher__sync-row">
                  <button
                    type="button"
                    className="account-switcher__sync-btn"
                    disabled={syncState[group.accounts[0].localIntegrationId]?.status === "loading"}
                    onClick={() => handleSync(group.accounts[0].localIntegrationId)}
                  >
                    {syncState[group.accounts[0].localIntegrationId]?.status === "loading"
                      ? "Iniciando..."
                      : syncState[group.accounts[0].localIntegrationId]?.status === "queued"
                      ? "Sincronização iniciada"
                      : "Sincronizar agora"}
                  </button>
                  {syncState[group.accounts[0].localIntegrationId]?.status === "error" && (
                    <span className="account-switcher__sync-error">
                      {syncState[group.accounts[0].localIntegrationId].message}
                    </span>
                  )}
                  <button
                    type="button"
                    className="account-switcher__disconnect"
                    onClick={() => {
                      setPendingRemoval({
                        kind: "connection",
                        id: group.accounts[0].localIntegrationId,
                        label: group.label ?? group.accounts[0].name,
                      });
                      setOpen(false);
                    }}
                  >
                    Desconectar banco
                  </button>
                </div>
              )}

              <div className="account-switcher__divider" />
            </div>
          ))}

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
