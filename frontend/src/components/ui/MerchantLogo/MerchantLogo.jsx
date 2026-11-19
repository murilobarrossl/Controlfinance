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

export default function MerchantLogo({ name, isTransfer = false, recurring = false }) {
  const brand = merchantBrand(name, { isTransfer });

  const base = recurring ? "recurring-logo" : "merchant-logo";
  if (!brand) {
    return (
      <span className={recurring ? "recurring-logo recurring-logo--glyph recurring-logo--neutral" : "merchant-logo merchant-logo--neutral"} aria-hidden="true">
        <NeutralGlyph transfer={isTransfer} />
      </span>
    );
  }

  const crop = recurring && brand.recurringViewBox;
  return (
    <span
      className={`${base}${crop || brand.wide ? ` ${base}--wide` : ""}`}
      style={brand.color ? { color: brand.color } : undefined}
      role="img"
      aria-label={brand.title}
      title={brand.title}
    >
      <svg viewBox={crop || brand.viewBox || "0 0 24 24"} fill="currentColor" aria-hidden="true">
        {brand.paths ? (
          <g transform={brand.transform}>
            {brand.paths.map((p, i) => <path key={i} d={p.d} fill={p.fill} fillRule={p.fillRule} />)}
          </g>
        ) : <path d={brand.path} fillRule={brand.fillRule} />}
      </svg>
    </span>
  );
}
