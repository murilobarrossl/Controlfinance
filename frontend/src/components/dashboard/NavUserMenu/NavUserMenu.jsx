import { useEffect, useRef, useState } from "react";
import Avatar from "../../ui/Avatar/Avatar.jsx";
import { ChevronDownIcon } from "../../ui/icons/FeatureIcons.jsx";
import "./NavUserMenu.css";

export default function NavUserMenu({ user, onLogout }) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef(null);

  useEffect(() => {
    function handleClickOutside(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false);
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const firstName = user?.name?.split(" ")[0];

  return (
    <div className="nav-user-menu" ref={rootRef}>
      <button
        type="button"
        className="nav-user-menu__trigger"
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
      >
        <Avatar name={user?.name} size="sm" />
        <span className="nav-user-menu__name">{firstName || "Conta"}</span>
        <span className={`nav-user-menu__chevron ${open ? "nav-user-menu__chevron--open" : ""}`}>
          <ChevronDownIcon />
        </span>
      </button>

      {open && (
        <div className="nav-user-menu__menu" role="menu">
          <button
            type="button"
            role="menuitem"
            className="nav-user-menu__option"
            onClick={() => {
              setOpen(false);
              onLogout();
            }}
          >
            Sair
          </button>
        </div>
      )}
    </div>
  );
}
