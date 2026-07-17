// Não dá mais pra decodificar o id do usuário de um JWT em localStorage (o token de sessão
// agora vive num cookie httpOnly, inacessível via JS de propósito). Essa função só serve pra
// segmentar chaves de localStorage não sensíveis por usuário (metas de investimento, reserva
// de emergência), então lê um id simples que o AuthContext mantém espelhado ali.
export function getCurrentUserId() {
  return localStorage.getItem("cf_current_user_id");
}
