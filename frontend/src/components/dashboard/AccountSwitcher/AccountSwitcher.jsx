import { useEffect, useRef, useState } from "react";
import { Link } from "react-router";
import { ChevronDownIcon, MoreIcon } from "../../ui/icons/FeatureIcons.jsx";
import ConfirmDialog from "../../ui/ConfirmDialog/ConfirmDialog.jsx";
import ManualAccountDialog from "../ManualAccountDialog/ManualAccountDialog.jsx";
import "./AccountSwitcher.css";

function Dot({ color }) {
  return <span className="account-switcher__dot" style={{ backgroundColor: color ?? "var(--color-border-strong)" }} />;
}

function ItemLabel({ item }) {
  return (
    <>
      <span className="account-switcher__name">{item.name}</span>
      {item.number && <span className="account-switcher__number">{item.number}</span>}
      <span className="account-switcher__kind">{item.kindLabel}</span>
    </>
  );
}

function ActionsMenu({ label, action, open, onToggle, onAction }) {
  return (
    <div className="account-switcher__actions">
      <button
        type="button"
        className="account-switcher__actions-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={`Opções de ${label}`}
        onClick={onToggle}
      >
        <MoreIcon />
      </button>
      {open && (
        <div className="account-switcher__actions-menu" role="menu">
          <button type="button" role="menuitem" className="account-switcher__actions-item" onClick={onAction}>
            {action}
          </button>
        </div>
      )}
    </div>
  );
}

export default function AccountSwitcher({ groups, activeScope, onSelect, onDisconnect, onRemoveManual, onAccountCreated }) {
  const [open, setOpen] = useState(false);
  const [actionsFor, setActionsFor] = useState(null);
  // { kind: "connection", id, label } | { kind: "manual", id, label } | null
  const [pendingRemoval, setPendingRemoval] = useState(null);
  const [manualAccountOpen, setManualAccountOpen] = useState(false);
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

  if (!activeScope) return null;

  function closeMenu() {
    setOpen(false);
    setActionsFor(null);
  }

  function requestRemoval(removal) {
    setPendingRemoval(removal);
    closeMenu();
  }

  function renderItem(item, manualGroup) {
    const active = item.key === activeScope.key;
    return (
      <div key={item.key} className={`account-switcher__row ${active ? "account-switcher__row--active" : ""}`}>
        <button
          type="button"
          role="menuitemradio"
          aria-checked={active}
          className="account-switcher__option"
          onClick={() => {
            if (!active) onSelect(item.key);
            closeMenu();
          }}
        >
          {manualGroup && <Dot color={item.color} />}
          <ItemLabel item={item} />
        </button>
        {manualGroup && item.accountId && onRemoveManual && (
          <ActionsMenu
            label={item.label}
            action="Remover conta"
            open={actionsFor === item.key}
            onToggle={() => setActionsFor(actionsFor === item.key ? null : item.key)}
            onAction={() => requestRemoval({ kind: "manual", id: item.accountId, label: item.label })}
          />
        )}
      </div>
    );
  }

  return (
    <div className="account-switcher" ref={rootRef}>
      <button
        type="button"
        className="account-switcher__trigger"
        onClick={() => (open ? closeMenu() : setOpen(true))}
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <Dot color={activeScope.color} />
        <ItemLabel item={activeScope} />
        <span className={`account-switcher__chevron ${open ? "account-switcher__chevron--open" : ""}`}>
          <ChevronDownIcon />
        </span>
      </button>

      {open && (
        <div className="account-switcher__menu" role="menu">
          {groups.map((group) => {
            if (!group.connectionId) return renderItem(group.items[0], true);

            const actionsOpen = actionsFor === group.key;
            return (
              <div key={group.key} className="account-switcher__group" role="group" aria-label={group.label}>
                <div className="account-switcher__group-header">
                  <Dot color={group.color} />
                  <p className="account-switcher__group-label">{group.label}</p>
                  <ActionsMenu
                    label={group.label}
                    action="Desconectar banco"
                    open={actionsOpen}
                    onToggle={() => setActionsFor(actionsOpen ? null : group.key)}
                    onAction={() => requestRemoval({ kind: "connection", id: group.connectionId, label: group.baseLabel })}
                  />
                </div>
                {group.items.map((item) => renderItem(item, false))}
              </div>
            );
          })}

          <div className="account-switcher__divider" />
          <Link to="/conectar-banco" className="account-switcher__option account-switcher__option--add">
            + Conectar outro banco
          </Link>
          <button
            type="button"
            className="account-switcher__option account-switcher__option--add"
            onClick={() => {
              setManualAccountOpen(true);
              setOpen(false);
            }}
          >
            + Cadastrar conta manual
          </button>
        </div>
      )}

      <ManualAccountDialog
        open={manualAccountOpen}
        onClose={() => setManualAccountOpen(false)}
        onCreated={onAccountCreated}
      />

      <ConfirmDialog
        open={pendingRemoval !== null}
        title={pendingRemoval?.kind === "connection" ? `Desconectar ${pendingRemoval?.label}?` : `Remover ${pendingRemoval?.label}?`}
        message={
          pendingRemoval?.kind === "connection"
            ? "Todas as contas e cartões desta conexão saem do Control Finance, e apagamos na hora o saldo, o extrato, os investimentos e as caixinhas que vieram desse banco. Também pedimos à Polp que exclua a conexão. Sua conta no Control Finance continua existindo. Para conectar de novo depois, é preciso autorizar outra vez no banco."
            : "A conta e os lançamentos dela serão apagados de forma definitiva."
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
