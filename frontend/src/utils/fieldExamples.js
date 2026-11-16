export const FIELD_EXAMPLES = {
  personName: "João da Silva",
  legalName: "Silva & Souza Ltda",
  tradeName: "Mercado Bom Preço",
  role: "Analista financeiro",
  department: "Financeiro",
  supplierCategory: "Contabilidade",
  cpf: "000.000.000-00",
  cnpj: "00.000.000/0000-00",
  cpfOrCnpj: "000.000.000-00 ou 00.000.000/0000-00",
  date: "dd/mm/aaaa",
  money: "0,00",
  bankCode: "341",
  branch: "1234",
  account: "12345-6",
};

export const PIX_KEY_EXAMPLES = {
  Cpf: "000.000.000-00",
  Cnpj: "00.000.000/0000-00",
  Email: "financeiro@empresa.com.br",
  Phone: "(11) 91234-5678",
  Random: "123e4567-e89b-12d3-a456-426614174000",
};

export const fieldExample = (example) => FIELD_EXAMPLES[example] ?? example;
