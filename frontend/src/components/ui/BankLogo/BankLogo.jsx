import { useState } from "react";
import { getLogoStyle } from "../../../utils/logoStyles.js";
import { cardTextColor } from "../../../utils/cardColors.js";
import "./BankLogo.css";

// Logo pequena da instituição, vinda do próprio conector da Polp (logoUrl), o mesmo usado na tela
// de conectar banco. Sem logo (ou se a imagem falhar), mostra as iniciais do banco na cor dele, em
// vez de uma imagem quebrada.
export default function BankLogo({ name = "", logoUrl, color, size = 28 }) {
  const [failed, setFailed] = useState(false);
  const { transparent, size: customSize } = getLogoStyle(name);
  const finalSize = customSize ?? size;
  const style = { width: finalSize, height: finalSize };

  if (!logoUrl || failed) {
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
      className={`bank-logo ${transparent ? "bank-logo--transparent" : ""}`}
      style={style}
      onError={() => setFailed(true)}
      loading="lazy"
      referrerPolicy="no-referrer"
    />
  );
}
