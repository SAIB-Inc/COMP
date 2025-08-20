using FastEndpoints;
using Comp.Modules.Handlers;
using Comp.Models.Request;

namespace Comp.Endpoints;

public class GetTokenMetadataEndpoint(MetadataHandler metadataHandler) : Endpoint<GetTokenMetadataRequest>
{
    private readonly MetadataHandler _metadataHandler = metadataHandler;

    public override void Configure()
    {
        Get("/metadata/{subject}");
        AllowAnonymous();
        Description(b => b
            .WithName("GetTokenMetadata")
            .WithSummary("Retrieve token metadata by subject")
            .WithTags("Metadata"));
    }

    public override async Task HandleAsync(GetTokenMetadataRequest req, CancellationToken ct)
    {
        var result = await _metadataHandler.GetTokenMetadataAsync(req.Subject);
        
        if (result is IResult httpResult)
        {
            await SendResultAsync(httpResult);
        }
    }
}
