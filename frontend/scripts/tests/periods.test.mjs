import assert from "node:assert/strict";
import { test } from "node:test";
import { PERIODS } from "../../src/utils/periods.js";

test("dia, mês e ano navegam em intervalos consecutivos sem sobreposição", () => {
  for (const period of Object.values(PERIODS)) {
    const current = period.current(0).range;
    const previous = period.previous(0).range;
    assert.equal(previous.to, current.from);
    assert.ok(current.from < current.to);
    assert.equal(period.current(1).range.from, previous.from);
  }
});

test("comparação anual usa ano inteiro também quando existem agendados", () => {
  assert.match(PERIODS.year.current(0).range.to, /-01-01$/);
  assert.match(PERIODS.year.previous(0).range.to, /-01-01$/);
});
