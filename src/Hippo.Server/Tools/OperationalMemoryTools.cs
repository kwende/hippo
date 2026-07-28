using System.ComponentModel;
using Hippo.Server.Authentication;
using Hippo.Server.Contracts;
using Hippo.Server.Data;
using ModelContextProtocol.Server;

namespace Hippo.Server.Tools;

[McpServerToolType]
public static class OperationalMemoryTools
{
    [McpServerTool(Name = "recall_operational_context")]
    [Description(
        """
        Recall compact shared operational context related to exact machine,
        customer, site, incident, software-version, hardware-revision, or
        component identifiers.

        Call this during diagnostic work after identifying relevant entities and
        before finalizing conclusions, even when the human did not explicitly
        ask for a memory lookup. Returned findings are evidence with status and
        confidence, not unquestionable truth.
        """
    )]
    public static Task<RecallResult> RecallOperationalContextAsync(
        [Description(
            "Structured identifiers, time range, and failure concepts for recall.")]
        RecallRequest request,
        MemoryRepository repository,
        CallerIdentityAccessor callerIdentityAccessor,
        CancellationToken cancellationToken)
    {
        return repository.RecallAsync(
            callerIdentityAccessor.RequireCurrent(),
            request,
            cancellationToken);
    }

    [McpServerTool(Name = "record_operational_findings")]
    [Description(
        """
        Record novel durable evidence-backed operational findings derived during
        the current task.

        Store compact claims, not conversations or raw logs. Distinguish direct
        observations and events from hypotheses and conclusions. Do not store
        credentials, routine plans, transient debugging chatter, or unsupported
        speculation. Repeated submissions are deduplicated.
        """
    )]
    public static Task<RecordFindingsResult> RecordOperationalFindingsAsync(
        [Description(
            "Source session and a small set of structured durable findings.")]
        RecordFindingsRequest request,
        MemoryRepository repository,
        CallerIdentityAccessor callerIdentityAccessor,
        CancellationToken cancellationToken)
    {
        return repository.RecordAsync(
            callerIdentityAccessor.RequireCurrent(),
            request,
            cancellationToken);
    }
}
