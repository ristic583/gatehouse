namespace CloudPacs.Simulator;

public sealed record WebhookSubscription(Guid Id, string Url, string Secret);
