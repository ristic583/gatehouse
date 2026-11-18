namespace Gatehouse.Agent.Execution;

/// <summary>The gateway lost the chunk sequence (<c>connector.export_chunk_out_of_order</c>); the export restarts from chunk 0.</summary>
public sealed class ExportRestartRequiredException() : Exception("The gateway requires the export to restart from chunk 0");
