using System.Security.Cryptography.X509Certificates;
using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Agent.Transport;
using Gatehouse.Connector.Contracts.Adapters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Agent;

public sealed class AgentWorker(
    AgentOptions options,
    IPacsAdapter adapter,
    AgentDatabase database,
    EnrollmentService enrollment,
    TimeProvider time,
    ILoggerFactory loggers) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = new Backoff();
        while (!stoppingToken.IsCancellationRequested && !enrollment.IsEnrolled)
        {
            try
            {
                await enrollment.EnrollAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                loggers.CreateLogger<AgentWorker>().LogError("Enrollment failed: {Reason}", ex.Message);
                await Task.Delay(backoff.Next(), time, stoppingToken);
            }
        }

        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(enrollment.PfxPath, null);
        using var http = ClientFactory.CreateHandlerClient(options, certificate);
        var client = new GatewayClient(http, options.AgentVersion);
        var pending = new PendingAckStore(database);
        var schedules = new ScheduledRevocationStore(database);
        var executor = new CommandExecutor(adapter, new ProcessedCommandStore(
            database), new GatewayExportSink(client, time, loggers.CreateLogger<GatewayExportSink>()), time,
            loggers.CreateLogger<CommandExecutor>(), pinDecryptor: new CertificatePinDecryptor(certificate), schedules: schedules,
            fences: new FenceStore(database));
        var revocations = new ScheduledRevocationLoop(adapter, schedules, time, loggers.CreateLogger<ScheduledRevocationLoop>());
        var poll = new PollLoop(client, executor, pending, time, loggers.CreateLogger<PollLoop>());
        var events = new PendingEventStore(database);
        var heartbeat = new HeartbeatLoop(
            client, adapter, pending, events, options, time, loggers.CreateLogger<HeartbeatLoop>(), schedules);
        var send = new EventSendLoop(client, events, time, loggers.CreateLogger<EventSendLoop>());
        var pump = new EventPumpLoop(adapter, events, send.Notify, time, loggers.CreateLogger<EventPumpLoop>());
        await Task.WhenAll(
            poll.RunAsync(stoppingToken), heartbeat.RunAsync(stoppingToken), pump.RunAsync(stoppingToken), send.RunAsync(stoppingToken),
            revocations.RunAsync(stoppingToken));
    }
}
