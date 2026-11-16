import BusinessModule, { ModuleSection, ModuleTable } from "./BusinessModule.jsx";

export default function Patrimonio() {
  return (
    <BusinessModule title="Patrimônio">
      <ModuleSection title="Bens">
        <ModuleTable
          columns={[
            { label: "Bem" },
            { label: "Categoria" },
            { label: "Descrição" },
            { label: "Valor registrado", numeric: true },
          ]}
        />
      </ModuleSection>

      <ModuleSection title="Categorias">
        <ModuleTable
          columns={[{ label: "Categoria" }, { label: "Bens", numeric: true }, { label: "Valor registrado", numeric: true }]}
        />
      </ModuleSection>
    </BusinessModule>
  );
}
