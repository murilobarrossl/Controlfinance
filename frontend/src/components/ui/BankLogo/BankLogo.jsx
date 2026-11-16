import { useState } from "react";
import { cardTextColor } from "../../../utils/cardColors.js";
import "./BankLogo.css";

// Logo pequena da instituição, vinda do próprio conector da Polp (logoUrl). Sem logo (ou se a
// imagem falhar), mostra as iniciais do banco na cor dele, em vez de uma imagem quebrada; com
// fallback="none" não mostra nada, para quando o nome do banco já está ao lado e as iniciais
// ficariam ilegíveis no tamanho usado.
// Logos são de servidores dos bancos e algumas URLs informadas pela Polp não carregam (domínio que
// não resolve, 403 contra uso fora do site deles). Uma URL que já falhou vai direto para as
// iniciais nas outras ocorrências da tela, em vez de repetir a tentativa a cada logo renderizada.
const failedLogoUrls = new Set();

export default function BankLogo({ name = "", logoUrl, color, size = 32, fallback = "initials" }) {
  const [failedUrl, setFailedUrl] = useState(null);
  const failed = failedUrl === logoUrl || failedLogoUrls.has(logoUrl);
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
      onError={() => {
        failedLogoUrls.add(logoUrl);
        setFailedUrl(logoUrl);
      }}
      loading="lazy"
      referrerPolicy="no-referrer"
    />
  );
}
