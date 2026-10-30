// Id do usuário logado, usado só pra separar por pessoa os dados que ficam no navegador
// (metas, reserva, conta selecionada) quando duas pessoas usam o mesmo computador. O token de
// sessão não fica mais no navegador (cookie httpOnly), então o AuthContext espelha aqui só o id,
// que é um identificador técnico sem dado pessoal.
export const CURRENT_USER_ID_KEY = "cf_current_user_id";

export function getCurrentUserId() {
  try {
    return localStorage.getItem(CURRENT_USER_ID_KEY);
  } catch {
    return null;
  }
}

export function setCurrentUserId(id) {
  try {
    if (id) localStorage.setItem(CURRENT_USER_ID_KEY, id);
    else localStorage.removeItem(CURRENT_USER_ID_KEY);
  } catch {
    // localStorage indisponível: os dados locais só não ficam separados por usuário
  }
}

// Apaga tudo que o Control Finance guardou neste navegador para o usuário informado (metas,
// reserva de emergência, conta selecionada) e a conexão bancária pendente.
export function clearLocalDataFor(userId) {
  try {
    const suffix = `:${userId}`;
    const keys = [];
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i);
      if (key && key.endsWith(suffix)) keys.push(key);
    }
    keys.forEach((key) => localStorage.removeItem(key));
    localStorage.removeItem("pendingPolpIntegrationId");
  } catch {
    // nada a limpar se o localStorage não estiver disponível
  }
}
