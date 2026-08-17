import type { RecorderIssue } from "../../../../schemas/generated/contracts.js";
import { stableId } from "./stable.js";

export function createIssue(
  code: RecorderIssue["code"],
  severity: RecorderIssue["severity"],
  title: string,
  technicalDetail: string,
  context: { eventId?: string; actionId?: string; omittedFromFlow?: boolean } = {}
): RecorderIssue {
  const id = stableId("issue", code, context.eventId ?? "", context.actionId ?? "");
  return {
    id,
    code,
    severity,
    ...(context.eventId === undefined ? {} : { eventId: context.eventId }),
    ...(context.actionId === undefined ? {} : { actionId: context.actionId }),
    title,
    technicalDetail: technicalDetail.slice(0, 2_000),
    evidenceIds: [],
    resolutionOptions: severity === "blocking" ? ["omit", "confirm"] : ["confirm"],
    omittedFromFlow: context.omittedFromFlow ?? false,
    resolved: false
  };
}

export function applyIssueResolutions(issues: RecorderIssue[], resolvedIds: string[]): RecorderIssue[] {
  const resolved = new Set(resolvedIds);
  return issues.map((issue) => resolved.has(issue.id) ? { ...issue, resolved: true } : issue);
}
