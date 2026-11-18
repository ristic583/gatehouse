import { apiClient } from "@/lib/api/client";
import type { FailFulfillmentDto, FulfillmentDetailDto, FulfillmentQueueItemDto } from "@/lib/api/types";

export const FULFILLMENTS_KEY = ["fulfillments"] as const;

export type FulfillmentView = "open" | "done";
export type FulfillmentAction = "start" | "confirm" | "fail" | "retry";

export const fulfillmentApi = {
  list: (view: FulfillmentView) => apiClient.get<FulfillmentQueueItemDto[]>(`/v1/fulfillments?view=${view}`),
  detail: (id: string) => apiClient.get<FulfillmentDetailDto>(`/v1/fulfillments/${id}`),
  act: (id: string, action: FulfillmentAction, body?: FailFulfillmentDto) =>
    apiClient.post<FulfillmentDetailDto>(`/v1/fulfillments/${id}/${action}`, body),
};
