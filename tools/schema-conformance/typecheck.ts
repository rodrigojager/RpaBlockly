import type {
  RpaBlocklyFlowV2,
  RpaBlocklyLocatorCatalogV1,
  RpaBlocklyPolicyV1
} from "../../schemas/generated/contracts.js";

const flow: RpaBlocklyFlowV2 = {
  schemaVersion: 2,
  name: "Conformidade TypeScript",
  actions: [
    {
      id: "wait",
      type: "wait",
      name: "Aguardar",
      target: { locatorId: "ready", cardinality: "single" }
    }
  ],
  subflows: {}
};

const locators: RpaBlocklyLocatorCatalogV1 = {
  schemaVersion: 1,
  locators: []
};

const policy: RpaBlocklyPolicyV1 = {
  schemaVersion: 1,
  locatorResilience: {
    mode: "strict",
    learningWriteBack: "disabled",
    promotion: "disabled",
    failedPrimary: "keep",
    minimumConfidence: 0.85,
    minimumRunnerUpGap: 0.1,
    maximumCandidatesPerLocator: 20,
    maximumHeuristicNodes: 5000,
    maximumResolutionMilliseconds: 30000
  }
};

void [flow, locators, policy];
