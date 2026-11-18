import type { FulfillmentStatus } from "@/lib/api/types";
import type { FulfillmentAction } from "./api";

export function availableActions(status: FulfillmentStatus): FulfillmentAction[] {
  switch (status) {
    case "Desired":
      return ["start", "confirm", "fail"];
    case "Pending":
      return ["confirm", "fail"];
    case "Failed":
      return ["retry"];
    default:
      return [];
  }
}
