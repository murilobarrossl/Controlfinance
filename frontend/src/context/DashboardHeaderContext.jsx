import { createContext, useContext } from "react";

// Elemento do cabeçalho do painel onde uma aba pode desenhar o próprio seletor (createPortal) no
// lugar do seletor global de contas, mantendo o estado dele dentro da própria aba.
export const DashboardHeaderSlotContext = createContext(null);

// eslint-disable-next-line react-refresh/only-export-components
export function useDashboardHeaderSlot() {
  return useContext(DashboardHeaderSlotContext);
}
