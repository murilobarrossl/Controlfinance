import { useId } from "react";

// Generic decorative EMV chip; no inference about contactless, validity or card hardware.
export function CardChip() {
  const gradientId = `chip-${useId().replace(/[^a-zA-Z0-9_-]/g, "")}`;
  return (
    <svg className="card-face__chip" viewBox="0 0 64 48" aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id={gradientId} x2="1" y2="1">
          <stop stopColor="#f2f1e9" />
          <stop offset=".42" stopColor="#bec1bc" />
          <stop offset=".7" stopColor="#e9eae2" />
          <stop offset="1" stopColor="#a1a6a1" />
        </linearGradient>
      </defs>
      <rect x="1" y="1" width="62" height="46" rx="8" fill={`url(#${gradientId})`} stroke="#777d77" />
      <g fill="none" stroke="#777d77" strokeWidth="1.1">
        <path d="M25 1v9q-9 5-9 14t9 14v9M39 1v9q9 5 9 14t-9 14v9" />
        <path d="M25 10h14M25 38h14M1 13h19M1 24h15M1 35h19M44 13h19M48 24h15M44 35h19" />
      </g>
      <rect x="2" y="2" width="60" height="44" rx="7" fill="none" stroke="#fff" strokeOpacity=".3" />
    </svg>
  );
}

// Network symbols only: no issuer artwork, product lookup or runtime image requests.
export function CardNetworkSymbol({ network }) {
  if (network?.id === "mastercard") {
    return (
      <svg className="card-face__network-symbol card-face__network-symbol--mastercard" viewBox="0 0 50 31" role="img" aria-label="Mastercard" focusable="false">
        <circle cx="15.5" cy="15.5" r="15.5" fill="#eb001b" />
        <circle cx="34.5" cy="15.5" r="15.5" fill="#f79e1b" />
        <path d="M25 3.25a15.5 15.5 0 0 1 0 24.5 15.5 15.5 0 0 1 0-24.5Z" fill="#ff5f00" />
      </svg>
    );
  }
  if (network?.id === "visa") {
    return (
      <svg className="card-face__network-symbol card-face__network-symbol--visa" viewBox="0 8 24 8" role="img" aria-label="Visa" focusable="false">
        {/* Visa contour from Simple Icons (CC0); network identification, not product art. */}
        <path fill="currentColor" d="M9.112 8.262L5.97 15.758H3.92L2.374 9.775c-.094-.368-.175-.503-.461-.658C1.447 8.864.677 8.627 0 8.479l.046-.217h3.3a.904.904 0 0 1 .894.764l.817 4.338 2.018-5.102Zm8.033 5.049c.008-1.979-2.736-2.088-2.717-2.972.006-.269.262-.555.822-.628a3.66 3.66 0 0 1 1.913.336l.34-1.59a5.207 5.207 0 0 0-1.814-.333c-1.917 0-3.266 1.02-3.278 2.479-.012 1.079.963 1.68 1.698 2.04.756.367 1.01.603 1.006.931-.005.504-.602.725-1.16.734-.975.015-1.54-.263-1.992-.473l-.351 1.642c.453.208 1.289.39 2.156.398 2.037 0 3.37-1.006 3.377-2.564Zm5.061 2.447H24l-1.565-7.496h-1.656a.883.883 0 0 0-.826.55l-2.909 6.946h2.036l.405-1.12h2.488Zm-2.163-2.656 1.02-2.815.588 2.815Zm-8.16-4.84-1.603 7.496H8.34l1.605-7.496Z" />
      </svg>
    );
  }
  return <span className="card-face__network-text">{network?.label || "Bandeira não informada"}</span>;
}
