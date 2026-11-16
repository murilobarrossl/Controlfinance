import { useState } from "react";
import FormDialog from "../../../components/ui/FormDialog/FormDialog.jsx";
import { TextField } from "../../../components/ui/FormDialog/FormField.jsx";
import { setEmployeeClassification } from "../../../api/employees.js";
import { EMPLOYMENT_TYPES } from "../../../utils/employees.js";
import useDialogSubmit from "./useDialogSubmit.js";

export function ClassificationDialog({ employee, departments, onClose, onSaved }) {
  const [department, setDepartment] = useState(employee.department ?? "");
  const [employmentType, setEmploymentType] = useState(employee.employmentType ?? "");
  const { submitting, error, submit } = useDialogSubmit(
    () => setEmployeeClassification(employee.id, { department: department.trim(), employmentType }),
    onSaved
  );

  return (
    <FormDialog open title="Vínculo e departamento" error={error} submitting={submitting} onSubmit={submit} onClose={onClose}>
      <div className="form-dialog__grid">
        <label className="form-dialog__field">
          <span className="form-dialog__label">Tipo de vínculo</span>
          <select value={employmentType} onChange={(e) => setEmploymentType(e.target.value)}>
            <option value="">Não informado</option>
            {EMPLOYMENT_TYPES.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
        </label>
        <TextField
          label="Departamento"
          example="department"
          value={department}
          onChange={(e) => setDepartment(e.target.value)}
          maxLength={80}
          list="employee-departments"
        />
        <datalist id="employee-departments">
          {departments.map((d) => (
            <option key={d} value={d} />
          ))}
        </datalist>
      </div>
    </FormDialog>
  );
}
