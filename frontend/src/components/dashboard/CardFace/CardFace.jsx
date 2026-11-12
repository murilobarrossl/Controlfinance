import { StarIcon } from "../../ui/icons/FeatureIcons.jsx";
import { cardNetwork } from "../../../utils/cardBrand.js";
import { cardPalette } from "../../../utils/cardColors.js";
import { CardChip, CardNetworkSymbol } from "./CardSymbols.jsx";
import "./CardFace.css";

function lastDigits(number) {
  const digits = String(number ?? "").replace(/\D/g, "");
  return digits.length >= 4 ? digits.slice(-4) : null;
}

// Both screens use this same family template. Names and issuer never select an appearance.
export default function CardFace({ card: suppliedCard, holder }) {
  const card = suppliedCard ?? {};
  const bank = card.bankName || (card.isSynced === false ? "Cartão manual" : "Banco não informado");
  const name = card.name || card.originalProductName;
  const network = cardNetwork(card.brand);
  const digits = lastDigits(card.number);
  const palette = cardPalette({ productType: card.productType });

  return (
    <span className="card-presentation">
      <span
        className="card-face"
        data-family={palette.family ?? "generic"}
        data-finish={palette.finish}
        style={{ "--card-base": palette.base, "--card-detail": palette.detail }}
      >
        <span className="card-face__issuer">
          <span className="card-face__bank" title={bank}>{bank}</span>
          {name && <span className="card-face__name" title={name}>{name}</span>}
        </span>

        <CardChip />

        <span className="card-face__number">
          {digits ? (
            <>
              <span aria-hidden="true">••••&ensp;</span>
              <span className="visually-hidden">Final </span>
              {digits}
            </>
          ) : (
            <span className="card-face__number-unknown">Final não informado</span>
          )}
        </span>

        <span className="card-face__holder" title={holder || undefined}>
          <span className="visually-hidden">Titular: </span>
          {holder || "—"}
        </span>

        <span className="card-face__network">
          <CardNetworkSymbol network={network} />
          {palette.label && <span className="card-face__category">{palette.label}</span>}
        </span>
      </span>

      {(card.dueDay || card.isPrimary) && (
        <span className="card-presentation__meta">
          {card.dueDay ? <span>Fatura vence dia {card.dueDay}</span> : null}
          {card.isPrimary && (
            <span className="card-presentation__primary"><StarIcon filled /> Principal</span>
          )}
        </span>
      )}
    </span>
  );
}
