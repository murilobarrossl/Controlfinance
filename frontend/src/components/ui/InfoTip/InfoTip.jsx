import { useId } from "react";
import "./InfoTip.css";

// Ícone "!" que explica um termo técnico só quando o usuário passa o mouse (ou foca com o teclado,
// ou toca no celular). A explicação fica no DOM o tempo todo e ligada ao botão por aria-describedby,
// então leitor de tela também lê; o CSS só decide quando ela aparece.
export default function InfoTip({ children, label = "O que é isso?", align = "center" }) {
  const id = useId();
  return (
    <span className={`info-tip info-tip--${align}`}>
      <button type="button" className="info-tip__button" aria-label={label} aria-describedby={id}>
        !
      </button>
      <span role="tooltip" id={id} className="info-tip__bubble">
        {children}
      </span>
    </span>
  );
}
