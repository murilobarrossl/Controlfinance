import { merchantBrand } from "../../../utils/merchantBrand.js";
import "./MerchantLogo.css";

function NeutralGlyph({ transfer }) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      {transfer ? (
        <path d="M7 7h11l-3-3M17 17H6l3 3" />
      ) : (
        <path d="M6 3h12v18l-3-2-3 2-3-2-3 2zM9 8h6M9 12h6" />
      )}
    </svg>
  );
}

export default function MerchantLogo({ name, isTransfer = false }) {
  const brand = merchantBrand(name, { isTransfer });

  if (!brand) {
    return (
      <span className="merchant-logo merchant-logo--neutral" aria-hidden="true">
        <NeutralGlyph transfer={isTransfer} />
      </span>
    );
  }

  return (
    <span
      className={`merchant-logo${brand.wide ? " merchant-logo--wide" : ""}`}
      style={brand.color ? { color: brand.color } : undefined}
      role="img"
      aria-label={brand.title}
      title={brand.title}
    >
      <svg viewBox={brand.viewBox ?? "0 0 24 24"} fill="currentColor" aria-hidden="true">
        <path d={brand.path} fillRule={brand.fillRule} />
      </svg>
    </span>
  );
}
