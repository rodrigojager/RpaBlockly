import assert from "node:assert/strict";
import test from "node:test";
import { authorLocator, isDynamicToken } from "../src/locators/authoring.js";
import { generatePackage } from "../src/package/generator.js";
import { validateGeneratedPackage } from "../src/package/validator.js";
import { rawEvent, targetSnapshot } from "./fixtures.js";

test("ranking preserva somente candidatos únicos, estáveis e não sensíveis", () => {
  const target = targetSnapshot({
    candidates: [
      { key: "xpath", expression: { strategy: "xpath", selector: "/html/body/input[1]" }, matchCount: 1, matchesTarget: true, sensitive: false, dynamic: false },
      { key: "testId", expression: { strategy: "testId", text: "customer-name" }, matchCount: 1, matchesTarget: true, sensitive: false, dynamic: false },
      { key: "label", expression: { strategy: "label", text: "token=claro" }, matchCount: 1, matchesTarget: true, sensitive: true, dynamic: false },
      { key: "shortCss", expression: { strategy: "css", selector: ".item" }, matchCount: 3, matchesTarget: false, sensitive: false, dynamic: false }
    ]
  });
  const authored = authorLocator(rawEvent(1, "click", { target }));
  assert.equal(authored?.candidates.length, 2);
  assert.equal(authored?.candidates[0]?.recipe.target.strategy, "testId");
  assert.equal(authored?.candidates[0]?.recorderRole, "capturedPrimary");
  assert.equal(authored?.candidates[1]?.recorderRole, "capturedAlternative");
  assert.doesNotMatch(JSON.stringify(authored), /token=claro/u);
  assert.equal(isDynamicToken("react-generated-1234567"), true);
});

test("gerador produz pacote V2 nativo com referências e policy conservadora", () => {
  const events = [
    rawEvent(1, "navigation", { target: undefined, targetKey: undefined, value: undefined }),
    rawEvent(2, "input", { value: "Maria" }),
    rawEvent(3, "click", {
      targetKey: "submit",
      target: targetSnapshot({
        tagName: "button",
        role: "button",
        accessibleName: "Enviar",
        candidates: [{
          key: "testId", expression: { strategy: "testId", text: "submit" },
          matchCount: 1, matchesTarget: true, sensitive: false, dynamic: false
        }]
      })
    })
  ];
  const generated = generatePackage("Cadastro", events);
  validateGeneratedPackage(generated);
  assert.equal(generated.flow.schemaVersion, 2);
  assert.equal(generated.flow.actions.length, 3);
  assert.equal(generated.flow.actions[1]?.valueSource?.startsWith("input.recorded."), true);
  assert.equal(generated.locators.locators[0]?.candidates[0]?.origin, "recorder");
  assert.equal(generated.policy.locatorResilience.mode, "strict");
  assert.equal(generated.policy.locatorResilience.promotion, "disabled");
});

test("validação standalone preserva a rejeição de documentos inválidos", () => {
  const generated = generatePackage("Cadastro", [rawEvent(1, "click")]);
  generated.flow.name = "";
  assert.throws(
    () => validateGeneratedPackage(generated),
    /flow\.production\.json inválido:/u
  );
});

test("segredo é representado somente por secret.recorded", () => {
  const generated = generatePackage("Login", [rawEvent(1, "input", {
    secretReference: "secret.recorded.value_0001"
  })]);
  validateGeneratedPackage(generated);
  assert.equal(generated.flow.actions[0]?.valueSource, "secret.recorded.value_0001");
  assert.doesNotMatch(JSON.stringify(generated), /senha-em-claro/u);
});

test("upload gera um data path válido sem caminho local do navegador", async () => {
  const generated = generatePackage("Upload", [rawEvent(1, "upload", {
    upload: {
      name: "comprovante final.pdf",
      mimeType: "application/pdf",
      size: 42,
      sha256: "0".repeat(64),
      included: false
    }
  })]);
  await validateGeneratedPackage(generated);
  assert.match(
    generated.flow.actions[0]?.valueSource ?? "",
    /^attachments\.recorded\.file_001_/u);
  assert.doesNotMatch(JSON.stringify(generated), /fakepath/iu);
});
