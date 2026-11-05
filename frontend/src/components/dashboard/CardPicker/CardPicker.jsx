import { useEffect, useId, useRef, useState } from "react";
import BankLogo from "../../ui/BankLogo/BankLogo.jsx";
import { CardIcon, ChevronDownIcon } from "../../ui/icons/FeatureIcons.jsx";
import "./CardPicker.css";

export default function CardPicker({ id, labelId, options, value, onChange, triggerLabel, ariaLabel }) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const rootRef = useRef(null);
  const buttonRef = useRef(null);
  const listRef = useRef(null);
  const listId = useId();
  const optionId = (index) => `${listId}-${index}`;
  const selectedIndex = Math.max(0, options.findIndex((o) => o.value === value));
  const selected = options[selectedIndex];

  useEffect(() => {
    if (!open) return undefined;
    listRef.current?.focus();
    listRef.current?.querySelector(`#${CSS.escape(`${listId}-${active}`)}`)?.scrollIntoView({ block: "nearest" });
    function handleOutside(e) {
      if (!rootRef.current?.contains(e.target)) setOpen(false);
    }
    document.addEventListener("mousedown", handleOutside);
    return () => document.removeEventListener("mousedown", handleOutside);
  }, [open, active, listId]);

  function openList() {
    setActive(selectedIndex);
    setOpen(true);
  }

  function choose(index) {
    onChange(options[index].value);
    setOpen(false);
    buttonRef.current?.focus();
  }

  function handleButtonKey(e) {
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      openList();
    }
  }

  function handleListKey(e) {
    const last = options.length - 1;
    const moves = { ArrowDown: Math.min(last, active + 1), ArrowUp: Math.max(0, active - 1), Home: 0, End: last };
    if (e.key in moves) {
      e.preventDefault();
      setActive(moves[e.key]);
    } else if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      choose(active);
    } else if (e.key === "Escape") {
      e.preventDefault();
      setOpen(false);
      buttonRef.current?.focus();
    } else if (e.key === "Tab") {
      setOpen(false);
    }
  }

  return (
    <div className={`card-picker${triggerLabel ? " card-picker--link" : ""}`} ref={rootRef}>
      <button
        ref={buttonRef}
        id={id}
        type="button"
        className={triggerLabel ? "card-picker__link" : "card-picker__trigger"}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        aria-label={ariaLabel}
        aria-labelledby={ariaLabel ? undefined : `${labelId} ${id}`}
        onClick={() => (open ? setOpen(false) : openList())}
        onKeyDown={handleButtonKey}
      >
        {triggerLabel ?? <PickerOption option={selected} />}
        <span className={`card-picker__chevron${open ? " card-picker__chevron--open" : ""}`} aria-hidden="true">
          <ChevronDownIcon />
        </span>
      </button>

      {open && (
        <ul
          ref={listRef}
          id={listId}
          className="card-picker__list"
          role="listbox"
          tabIndex={0}
          aria-labelledby={ariaLabel ? undefined : labelId}
          aria-label={ariaLabel}
          aria-activedescendant={optionId(active)}
          onKeyDown={handleListKey}
        >
          {options.map((option, index) => (
            <li
              key={option.value}
              id={optionId(index)}
              role="option"
              aria-selected={option.value === value}
              className={`card-picker__option${index === active ? " card-picker__option--active" : ""}`}
              style={{ "--bank-color": option.color ?? "var(--color-border-strong)" }}
              onMouseEnter={() => setActive(index)}
              onClick={() => choose(index)}
            >
              <PickerOption option={option} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function PickerOption({ option }) {
  return (
    <span className="card-picker__content">
      {option.logo ? (
        <BankLogo name={option.logo.name} logoUrl={option.logo.logoUrl} color={option.logo.color} size={28} />
      ) : (
        <span className="card-picker__icon" aria-hidden="true">
          <CardIcon />
        </span>
      )}
      <span className="card-picker__text">
        <span className="card-picker__title">{option.title}</span>
        <span className="card-picker__subtitle">{option.subtitle}</span>
      </span>
    </span>
  );
}
