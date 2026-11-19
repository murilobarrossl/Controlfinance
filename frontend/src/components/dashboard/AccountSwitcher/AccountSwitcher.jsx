import { useEffect, useId, useRef, useState } from "react";
import { Link } from "react-router";
import { ChevronDownIcon, MoreIcon, PlusIcon } from "../../ui/icons/FeatureIcons.jsx";
import ConfirmDialog from "../../ui/ConfirmDialog/ConfirmDialog.jsx";
import FormDialog from "../../ui/FormDialog/FormDialog.jsx";
import { useAuth } from "../../../context/AuthContext.jsx";
import { isBusinessHolder } from "../../../utils/holder.js";
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

function DefaultAccountLabel({ account }) {
  return (
    <>
      <Dot color={account.color} />
      <span className="account-switcher__default-bank">{account.bank}</span>
      <span className="account-switcher__default-account">{account.maskedNumber ?? account.kindLabel}</span>
    </>
  );
}

function defaultAccountCandidates(groups) {
  return groups.flatMap((g) => (g.connectionId ? g.items.map((item) => ({ ...item, bank: g.baseLabel })) : []));
}

function DefaultAccountDialog({ accounts, onChange, onClose }) {
  const current = accounts.find((a) => a.isDefault) ?? null;
  const [selected, setSelected] = useState(current?.accountId ?? null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  async function handleSubmit(e) {
    e.preventDefault();
    if (!selected) {
      setError("Escolha uma conta.");
      return;
    }
    if (selected === current?.accountId) {
      onClose();
      return;
    }
    setSaving(true);
    setError(null);
    const saved = await onChange(selected);
    setSaving(false);
    if (saved) onClose();
    else setError("Não foi possível alterar a conta padrão. Tente novamente.");
  }

  return (
    <FormDialog
      open
      title="Selecionar conta padrão de pagamento"
      error={error}
      submitting={saving}
      onSubmit={handleSubmit}
      onClose={() => {
        if (!saving) onClose();
      }}
      showCloseButton
    >
      <fieldset className="default-account-dialog__list" disabled={saving}>
        <legend className="visually-hidden">Contas disponíveis</legend>
        {accounts.map((a) => (
          <label key={a.key} className={`default-account-dialog__option${a.isDefault ? " default-account-dialog__option--current" : ""}`}>
            <input
              type="radio"
              name="default-account"
              value={a.accountId}
              checked={selected === a.accountId}
              onChange={() => {
                setSelected(a.accountId);
                setError(null);
              }}
            />
            <Dot color={a.color} />
            <span className="default-account-dialog__text">
              <span className="default-account-dialog__bank">{a.bank}</span>
              <span className="default-account-dialog__meta">
                {[a.kindLabel, a.maskedNumber].filter(Boolean).join(" · ")}
              </span>
            </span>
            {a.isDefault && <span className="default-account-dialog__current-tag">Padrão atual</span>}
          </label>
        ))}
      </fieldset>
    </FormDialog>
  );
}

function DefaultAccountSection({ groups, onOpenPicker, renderCurrent }) {
  const accounts = defaultAccountCandidates(groups);
  const current = accounts.find((a) => a.isDefault) ?? null;
  const eligible = accounts.filter((a) => a.canBeDefault && !a.isDefault);

  if (accounts.length === 0) return null;

  return (
    <section className="account-switcher__default-section" aria-labelledby="account-switcher-default-title">
      <div className="account-switcher__default-header">
        <p id="account-switcher-default-title" className="account-switcher__group-label">Conta padrão de pagamentos</p>
        {current && eligible.length > 0 && (
          <button type="button" className="account-switcher__default-change" aria-haspopup="dialog" onClick={onOpenPicker}>
            Mudar banco padrão
          </button>
        )}
      </div>
      {current ? (
        renderCurrent(current)
      ) : (
        <button
          type="button"
          className="account-switcher__default-add"
          aria-label="Definir conta padrão de pagamentos"
          title="Definir conta padrão de pagamentos"
          disabled={eligible.length === 0}
          aria-haspopup="dialog"
          onClick={onOpenPicker}
        >
          <PlusIcon />
        </button>
      )}
    </section>
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

export default function AccountSwitcher({ groups, activeScope, onSelect, onDisconnect, onRemoveManual, onChangeDefault, caption }) {
  const captionId = useId();
  const { user } = useAuth();
  const closingsNote = isBusinessHolder(user) ? " Fechamentos de caixa que usaram esses dados também são apagados." : "";
  const [open, setOpen] = useState(false);
  const [actionsFor, setActionsFor] = useState(null);
  // { kind: "connection", id, label } | { kind: "manual", id, label } | null
  const [pendingRemoval, setPendingRemoval] = useState(null);
  const [pickingDefault, setPickingDefault] = useState(false);
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
    <div className={`account-switcher${caption ? " account-switcher--captioned" : ""}`} ref={rootRef}>
      {caption && <span id={captionId} className="account-switcher__caption">{caption}</span>}
      <button
        type="button"
        className="account-switcher__trigger"
        aria-describedby={caption ? captionId : undefined}
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
                {group.items.filter((item) => !onChangeDefault || !item.isDefault).map((item) => renderItem(item, false))}
              </div>
            );
          })}

          {onChangeDefault && (
            <>
              <div className="account-switcher__divider" />
              <DefaultAccountSection
                groups={groups}
                onOpenPicker={() => {
                  closeMenu();
                  setPickingDefault(true);
                }}
                renderCurrent={(current) => {
                  const active = current.key === activeScope.key;
                  return (
                    <div className={`account-switcher__row ${active ? "account-switcher__row--active" : ""}`}>
                      <button
                        type="button"
                        role="menuitemradio"
                        aria-checked={active}
                        className="account-switcher__option account-switcher__default-current"
                        onClick={() => {
                          if (!active) onSelect(current.key);
                          closeMenu();
                        }}
                      >
                        <DefaultAccountLabel account={current} />
                      </button>
                    </div>
                  );
                }}
              />
            </>
          )}

          <div className="account-switcher__divider" />
          <Link to="/conectar-banco" className="account-switcher__option account-switcher__option--add">
            + Conectar outro banco
          </Link>
        </div>
      )}

      {pickingDefault && onChangeDefault && (
        <DefaultAccountDialog
          accounts={defaultAccountCandidates(groups).filter((a) => a.canBeDefault)}
          onChange={onChangeDefault}
          onClose={() => setPickingDefault(false)}
        />
      )}

      <ConfirmDialog
        confirmVariant="danger"
        open={pendingRemoval !== null}
        title={pendingRemoval?.kind === "connection" ? `Desconectar ${pendingRemoval?.label}?` : `Remover ${pendingRemoval?.label}?`}
        message={
          pendingRemoval?.kind === "connection"
            ? `Todas as contas e cartões desta conexão saem do Control Finance, e apagamos na hora o saldo, o extrato, os investimentos e as caixinhas que vieram desse banco.${closingsNote} Também solicitamos ao serviço de integração bancária a exclusão da conexão. Sua conta no Control Finance continua existindo. Para conectar de novo depois, é preciso autorizar outra vez no banco.`
            : `A conta e os lançamentos dela serão apagados de forma definitiva.${closingsNote}`
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
