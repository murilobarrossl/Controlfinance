// Espelho de backend/DTOs/AuthDtos.cs (RegisterDto.Password e RegisterDto.Document) e de
// backend/Services/DocumentValidator.cs. O backend continua sendo a validação de verdade:
// mudou lá, muda aqui.

export const PASSWORD_PATTERN = /^(?=.*[A-Za-z])(?=.*\d).{8,}$/;
export const PASSWORD_MESSAGE = "A senha deve ter no mínimo 8 caracteres, incluindo letras e números.";

const DOCUMENT_PATTERN = /^\d{11}(\d{3})?$/;
export const DOCUMENT_FORMAT_MESSAGE = "Documento inválido. Informe CPF (11 dígitos) ou CNPJ (14 dígitos).";
export const DOCUMENT_INVALID_MESSAGE = "CPF/CNPJ inválido.";

function allSameDigit(doc) {
  return new Set(doc).size === 1;
}

function checkDigit(digits, count, firstWeight) {
  let sum = 0;
  for (let i = 0; i < count; i++) sum += digits[i] * (firstWeight - i);
  const remainder = sum % 11;
  return remainder < 2 ? 0 : 11 - remainder;
}

function checkDigitWeighted(digits, count, weights) {
  let sum = 0;
  for (let i = 0; i < count; i++) sum += digits[i] * weights[i];
  const remainder = sum % 11;
  return remainder < 2 ? 0 : 11 - remainder;
}

export function isValidCpf(cpf) {
  if (!/^\d{11}$/.test(cpf) || allSameDigit(cpf)) return false;
  const digits = [...cpf].map(Number);
  if (digits[9] !== checkDigit(digits, 9, 10)) return false;
  return digits[10] === checkDigit(digits, 10, 11);
}

export function isValidCnpj(cnpj) {
  if (!/^\d{14}$/.test(cnpj) || allSameDigit(cnpj)) return false;
  const digits = [...cnpj].map(Number);
  const weights1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
  const weights2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
  if (digits[12] !== checkDigitWeighted(digits, 12, weights1)) return false;
  return digits[13] === checkDigitWeighted(digits, 13, weights2);
}

export function documentError(rawDocument) {
  const digits = rawDocument.replace(/\D/g, "");
  if (!DOCUMENT_PATTERN.test(digits)) return DOCUMENT_FORMAT_MESSAGE;
  const valid = digits.length === 11 ? isValidCpf(digits) : isValidCnpj(digits);
  return valid ? null : DOCUMENT_INVALID_MESSAGE;
}

export function passwordError(password) {
  return PASSWORD_PATTERN.test(password) ? null : PASSWORD_MESSAGE;
}
