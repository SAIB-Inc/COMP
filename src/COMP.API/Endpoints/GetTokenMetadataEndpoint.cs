using FastEndpoints;
using COMP.API.Modules.Handlers;
using COMP.Data.Models.Request;

namespace COMP.API.Endpoints;

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
        IResult result = await _metadataHandler.GetTokenMetadataAsync(req.Subject);
        
        if (result is IResult httpResult)
        {
            await SendResultAsync(httpResult);
        }
    }
}
