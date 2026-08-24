const controls = {
  mode: "policy-mode",
  learningWriteBack: "policy-learning-write-back",
  promotion: "policy-promotion",
  failedPrimary: "policy-failed-primary",
  minimumConfidence: "policy-minimum-confidence",
  minimumRunnerUpGap: "policy-minimum-runner-up-gap",
  maximumCandidatesPerLocator: "policy-maximum-candidates",
  maximumHeuristicNodes: "policy-maximum-heuristic-nodes",
  maximumResolutionMilliseconds: "policy-maximum-resolution-ms"
};

export function initializePolicyUi(onApply, onError) {
  element(controls.mode).addEventListener("change", synchronizeDependencies);
  element(controls.learningWriteBack).addEventListener("change", synchronizeDependencies);
  document.getElementById("save-policy-draft").addEventListener("click", () => {
    try {
      onApply(readPolicy());
    } catch (error) {
      onError(error);
    }
  });
}

export function renderPolicyUi(policy) {
  const resilience = policy?.locatorResilience;
  if (!resilience) return;
  for (const [property, id] of Object.entries(controls)) {
    element(id).value = String(resilience[property]);
  }
  synchronizeDependencies();
  document.getElementById("policy-json").value = JSON.stringify(policy, null, 2);
}

function readPolicy() {
  const resilience = {
    mode: value(controls.mode),
    learningWriteBack: value(controls.learningWriteBack),
    promotion: value(controls.promotion),
    failedPrimary: value(controls.failedPrimary),
    minimumConfidence: number(controls.minimumConfidence, false),
    minimumRunnerUpGap: number(controls.minimumRunnerUpGap, false),
    maximumCandidatesPerLocator: number(controls.maximumCandidatesPerLocator, true),
    maximumHeuristicNodes: number(controls.maximumHeuristicNodes, true),
    maximumResolutionMilliseconds: number(controls.maximumResolutionMilliseconds, true)
  };
  return { schemaVersion: 1, locatorResilience: resilience };
}

function synchronizeDependencies() {
  const adaptive = value(controls.mode) === "adaptive";
  const learning = element(controls.learningWriteBack);
  const promotion = element(controls.promotion);
  if (!adaptive) {
    learning.value = "disabled";
    promotion.value = "disabled";
  }
  learning.disabled = !adaptive;
  if (learning.value === "disabled") promotion.value = "disabled";
  promotion.disabled = !adaptive || learning.value === "disabled";
}

function number(id, integer) {
  const control = element(id);
  if (control.value.trim() === "" || !Number.isFinite(control.valueAsNumber)) {
    throw new Error(`${control.closest("label")?.firstChild?.textContent?.trim() ?? id} exige um número.`);
  }
  if (integer && !Number.isInteger(control.valueAsNumber)) {
    throw new Error(`${control.closest("label")?.firstChild?.textContent?.trim() ?? id} exige um inteiro.`);
  }
  return control.valueAsNumber;
}

function value(id) {
  return element(id).value;
}

function element(id) {
  return document.getElementById(id);
}
