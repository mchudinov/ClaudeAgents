using Foundry.Agents.Contracts.Mcp;

namespace Foundry.Agents.Reviewer.Verdict;

public static class VerdictMapper
{
    public static ReviewVerdict FromModelOutput(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return raw.Trim().ToUpperInvariant() switch
        {
            "APPROVE" or "APPROVED"                                               => ReviewVerdict.Approved,
            "REQUEST_CHANGES" or "CHANGES_REQUESTED"                              => ReviewVerdict.ChangesRequested,
            "REJECT" or "REJECTED" or "REJECTED_BLOCKING" or "BLOCKING"           => ReviewVerdict.RejectedBlocking,
            var v => throw new InvalidOperationException($"unrecognized verdict from model: '{v}'"),
        };
    }
}
