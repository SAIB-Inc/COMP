using Comp.Sync.Data;
using Comp.Sync.Data.Models;
using Comp.Sync.Data.Models.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Comp.Sync.Controllers;

[ApiController]
[Route("metadata/token")]
public class TokenMetadataController : ControllerBase
{
    private readonly ILogger<TokenMetadataController> _logger;
    private readonly IDbContextFactory<TokenMetadataDbContext> _dbFactory;

    public TokenMetadataController(
        ILogger<TokenMetadataController> logger,
        IDbContextFactory<TokenMetadataDbContext> dbFactory)
    {
        _logger = logger;
        _dbFactory = dbFactory;
    }

    // GET: /metadata/token/{subject}
    [HttpGet("{subject}")]
    public async Task<IActionResult> Get(string subject)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var metadataEntry = await db.TokenMetadata
            .FirstOrDefaultAsync(tmd => tmd.Subject.ToLower() == subject.ToLower());

        if (metadataEntry is not null)
        {
            return Ok(metadataEntry);
        }
        else
        {
            return NotFound();
        }
    }

    // POST: /metadata/token
    [HttpPost]
    public async Task<ActionResult<PaginatedResponse<TokenMetadata>>> Search([FromQuery] SearchTokenMetadataDto search)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();
        var query = context.TokenMetadata.AsQueryable();
        
        if (!string.IsNullOrWhiteSpace(search.Name))
            query = query.Where(t => EF.Functions.ILike(t.Name, $"%{search.Name}%"));
        
        if (!string.IsNullOrWhiteSpace(search.Description))
            query = query.Where(t => EF.Functions.ILike(t.Description, $"%{search.Description}%"));
        
        if (!string.IsNullOrWhiteSpace(search.Ticker))
            query = query.Where(t => EF.Functions.ILike(t.Ticker, $"%{search.Ticker}%"));
        
        if (search.Decimals.HasValue)
            query = query.Where(t => t.Decimals == search.Decimals.Value);

        query = search.SortBy?.ToLower() switch
        {
            "name" => search.SortDescending ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
            "ticker" => search.SortDescending ? query.OrderByDescending(t => t.Ticker) : query.OrderBy(t => t.Ticker),
            "decimals" => search.SortDescending ? query.OrderByDescending(t => t.Decimals) : query.OrderBy(t => t.Decimals),
            _ => search.SortDescending ? query.OrderByDescending(t => t.Subject) : query.OrderBy(t => t.Subject)
        };

        var totalCount = await query.CountAsync();
        var items = await query
            .Skip(search.Offset)
            .Take(search.Limit)
            .ToListAsync();

        return new PaginatedResponse<TokenMetadata>
        {
            Items = items,
            Offset = search.Offset,
            Limit = search.Limit,
            TotalCount = totalCount
        };
    }
}
