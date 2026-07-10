export const applyMask = (value, mask, prev) => {
  const isDeleting = prev && value.length < prev.length;
  const digits = value.replace(/\D/g, "");
  if (isDeleting && digits.length === 0) return "";

  let i = 0;
  let result = "";
  for (let j = 0; j < mask.length; j++) {
    if (i >= digits.length) break;
    if (mask[j] === "#") {
      result += digits[i++];
    } else {
      result += mask[j];
    }
  }
  return result;
};

export const CPF_MASK = "###.###.###-##";
export const CNPJ_MASK = "##.###.###/####-##";
export const getCpfCnpjMask = (digits) => (digits.length <= 11 ? CPF_MASK : CNPJ_MASK);

export const PHONE_MASK_LANDLINE = "(##) ####-####";
export const PHONE_MASK_MOBILE = "(##) #####-####";
export const getPhoneMask = (digits) =>
  digits.length <= 10 ? PHONE_MASK_LANDLINE : PHONE_MASK_MOBILE;
