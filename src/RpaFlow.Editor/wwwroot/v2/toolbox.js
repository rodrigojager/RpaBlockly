import { actionCatalog } from "./action-catalog.js";
import { FieldLocatorReference } from "./field-locator-reference.js";

const colours = {
  "Navegação": 205,
  "Esperas": 45,
  "Formulários": 165,
  "Leitura": 260,
  "Dados e controle": 120,
  "Controle": 25,
  "Subfluxos": 300,
  "Arquivos e evidências": 5
};

const roleLabels = {
  target: "alvo",
  trigger: "gatilho",
  options: "opções",
  ready: "prontidão",
  success: "sucesso",
  protocol: "protocolo",
  condition: "condição"
};

export function registerBlocks() {
  for (const definition of actionCatalog) {
    Blockly.Blocks[definition.blockType] = {
      init() {
        if (definition.structural === "subflow") {
          this.appendDummyInput()
            .appendField(definition.label)
            .appendField(new Blockly.FieldTextInput("subflow"), "SUBFLOW");
          this.appendStatementInput("ACTIONS").appendField("ações");
          this.setColour(colours[definition.category]);
          this.setTooltip("Define uma sequência reutilizável do fluxo V2.");
          return;
        }

        this.appendDummyInput()
          .appendField(definition.label)
          .appendField(new Blockly.FieldTextInput(definition.label), "NAME");
        this.appendDummyInput()
          .appendField("ID")
          .appendField(new Blockly.FieldTextInput("acao"), "ID");
        for (const role of definition.roles) {
          this.appendDummyInput()
            .appendField(`locator de ${roleLabels[role]}`)
            .appendField(new FieldLocatorReference(), fieldName(role))
            .appendField("cardinalidade")
            .appendField(new Blockly.FieldDropdown([
              ["um", "single"],
              ["primeiro", "first"],
              ["muitos", "many"]
            ]), cardinalityFieldName(role));
        }

        if (["if", "repeat", "forEach"].includes(definition.structural)) {
          this.appendStatementInput("ACTIONS").appendField("ações");
        }
        if (definition.structural === "if") {
          this.appendStatementInput("ELSE_ACTIONS").appendField("senão");
        }
        this.setPreviousStatement(true);
        this.setNextStatement(true);
        this.setColour(colours[definition.category]);
        this.setTooltip(
          "O bloco guarda somente referências de locator. A receita fica no catálogo V2.");
      }
    };
  }
}

export function createToolbox() {
  const categories = [];
  for (const category of Object.keys(colours)) {
    const contents = actionCatalog
      .filter(item => item.category === category)
      .map(item => ({ kind: "block", type: item.blockType }));
    categories.push({
      kind: "category",
      name: category,
      colour: colours[category],
      contents
    });
  }
  return { kind: "categoryToolbox", contents: categories };
}

export function fieldName(role) {
  return `LOCATOR_${role.toUpperCase()}`;
}

export function cardinalityFieldName(role) {
  return `${fieldName(role)}_CARDINALITY`;
}
