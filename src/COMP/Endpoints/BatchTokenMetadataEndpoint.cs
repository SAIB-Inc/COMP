using FastEndpoints;
using Comp.Modules.Handlers;
using Comp.Models.Request;

namespace Comp.Endpoints;

public class BatchTokenMetadataEndpoint(MetadataHandler metadataHandler) : Endpoint<BatchTokenMetadataRequest>
{
    private readonly MetadataHandler _metadataHandler = metadataHandler;

    public override void Configure()
    {
        Post("/metadata");
        AllowAnonymous();
        Description(b => b
            .WithName("BatchTokenMetadata")
            .WithSummary("Retrieve token metadata for a batch of subjects")
            .WithTags("Metadata"));
    }

    public override async Task HandleAsync(BatchTokenMetadataRequest req, CancellationToken ct)
    {
        var result = await _metadataHandler.BatchTokenMetadataAsync(
            req.Subjects,
            req.Limit,
            req.SearchText,
            req.PolicyId,
            req.Policy,
            req.Offset,
            req.IncludeEmptyName,
            req.IncludeEmptyLogo,
            req.IncludeEmptyTicker);
        
        if (result is IResult httpResult)
        {
            await SendResultAsync(httpResult);
        }
    }
}
