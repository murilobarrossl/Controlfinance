import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import "./AccountSwitcher.css";

function ChevronIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path
        d="M5.70711 9.71069C5.31658 10.1012 5.31658 10.7344 5.70711 11.1249L10.5993 16.0123C11.3805 16.7927 12.6463 16.7924 13.4271 16.0117L18.3174 11.1213C18.708 10.7308 18.708 10.0976 18.3174 9.70708C17.9269 9.31655 17.2937 9.31655 16.9032 9.70708L12.7176 13.8927C12.3271 14.2833 11.6939 14.2832 11.3034 13.8927L7.12132 9.71069C6.7308 9.32016 6.09763 9.32016 5.70711 9.71069Z"
        fill="currentColor"
      />
    </svg>
  );
}

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
          <ChevronIcon />
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
