namespace Gatehouse.Application.People.Admin.ImportPeople;

public sealed record ImportPeopleCommand(byte[] File, bool DryRun);
