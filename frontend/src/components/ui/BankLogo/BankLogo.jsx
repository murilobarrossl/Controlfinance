import { useState } from "react";
import { cardTextColor } from "../../../utils/cardColors.js";
import "./BankLogo.css";

// Logo pequena da instituição, vinda do próprio conector da Polp (logoUrl). Sem logo (ou se a
// imagem falhar), mostra as iniciais do banco na cor dele, em vez de uma imagem quebrada; com
// fallback="none" não mostra nada, para quando o nome do banco já está ao lado e as iniciais
// ficariam ilegíveis no tamanho usado.
export default function BankLogo({ name = "", logoUrl, color, size = 32, fallback = "initials" }) {
  const [failed, setFailed] = useState(false);
  const style = { "--bank-logo-size": `${size}px` };

  if (!logoUrl || failed) {
    if (fallback === "none") return null;
    const initials = name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0])
      .join("")
      .toUpperCase();
    const hex = color ? (color.startsWith("#") ? color : `#${color}`) : null;
    const colors = /^#[0-9a-f]{6}$/i.test(hex ?? "")
      ? { backgroundColor: hex, color: cardTextColor(hex) }
      : { backgroundColor: "var(--color-border-strong)" };
    return (
      <span className="bank-logo bank-logo--fallback" style={{ ...style, ...colors }} aria-hidden="true">
        {initials || "?"}
      </span>
    );
  }

  // A imagem vem de um servidor de terceiros (URL informada pela Polp): sem Referer, ele não
  // fica sabendo de qual página do painel o pedido saiu.
  return (
    <img
      src={logoUrl}
      alt=""
      className="bank-logo"
      style={style}
      width={size}
      height={size}
      onError={() => setFailed(true)}
      loading="lazy"
      referrerPolicy="no-referrer"
    />
  );
}
