import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { ChevronDownIcon } from "../../ui/icons/FeatureIcons.jsx";
import "./AccountSwitcher.css";

// Mesma cor usada na tarja ao lado de cada banco na tela de conectar-banco, nunca
// escolhida à mão aqui, só repassada do conector correspondente (via bankCode).
function resolveAccountColor(account, connectorsById) {
  const raw = connectorsById.get(account.bankCode)?.color;
  if (!raw) return "var(--color-border-strong)";
  return raw.startsWith("#") ? raw : `#${raw}`;
}

export default function AccountSwitcher({ accounts, activeAccountId, connectors, onSelect }) {
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
  const others = accounts.filter((a) => a.id !== activeAccount.id);

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
          {others.map((account) => (
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
          ))}

          {others.length > 0 && <div className="account-switcher__divider" />}

          <Link to="/conectar-banco" className="account-switcher__option account-switcher__option--add">
            + Conectar outro banco
          </Link>
        </div>
      )}
    </div>
  );
}
