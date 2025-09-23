using Argus.Sync.Reducers;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Microsoft.EntityFrameworkCore;
using Comp.Models;
using Comp.Models.Entity;
using System.Text;
using System.Text.RegularExpressions;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Comp.Sync.Reducers;

public partial class CIP25Reducer(
    IDbContextFactory<MetadataDbContext> dbContextFactory,
    ILogger<CIP25Reducer> logger
) : IReducer<TokenMetadataOnChain>
{
    private static readonly string[] ValidSchemes = ["https://", "http://", "ipfs://", "ar://", "data:"];
    private static readonly Regex DataUriRegex = MyRegex();

    public async Task RollBackwardAsync(ulong slot)
    {
        logger.LogWarning("Rollback requested to slot {Slot}. Manual resync may be required as we don't maintain historical state.", slot);
        await Task.CompletedTask;
    }

    public async Task RollForwardAsync(Block block)
    {
        List<TransactionBody> txBodies = [.. block.TransactionBodies()];
        Dictionary<int, AuxiliaryData> auxiliaryDataDict = block.AuxiliaryDataSet().ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        Dictionary<string, TokenMetadataOnChain> tokensWithMetadata = [];

        for (int i = 0; i < txBodies.Count; i++)
        {
            TransactionBody tx = txBodies[i];
            Dictionary<byte[], TokenBundleMint>? mint = tx.Mint();
            if (mint == null || mint.Count == 0)
                continue;

            if (!auxiliaryDataDict.TryGetValue(i, out AuxiliaryData? auxData))
                continue;

            Metadata? metadata = auxData.Metadata();
            if (metadata == null)
                continue;

            if (!metadata.Value().TryGetValue(721, out TransactionMetadatum? cip25Value))
                continue;

            if (cip25Value is not MetadatumMap rootMap)
                continue;

            foreach (KeyValuePair<byte[], TokenBundleMint> policyEntry in mint)
            {
                string policyId = Convert.ToHexString(policyEntry.Key).ToLowerInvariant();

                foreach (KeyValuePair<byte[], long> assetEntry in policyEntry.Value.Value.Where(a => a.Value > 0))
                {
                    string assetNameHex = Convert.ToHexString(assetEntry.Key).ToLowerInvariant();
                    string subject = $"{policyId}{assetNameHex}";

                    TokenMetadataOnChain? tokenMetadata = ExtractMetadata(rootMap, policyId, assetNameHex, assetEntry.Value);

                    if (tokenMetadata == null)
                        continue;

                    if (string.IsNullOrEmpty(tokenMetadata.Name) || string.IsNullOrEmpty(tokenMetadata.Logo))
                    {
                        logger.LogWarning("Skipping {Subject} - CIP-25 requires name and image fields", subject);
                        continue;
                    }

                    tokensWithMetadata[subject] = tokenMetadata;
                }
            }
        }

        if (tokensWithMetadata.Count == 0)
            return;

        await using MetadataDbContext db = await dbContextFactory.CreateDbContextAsync();

        List<string> existingSubjects = [.. tokensWithMetadata.Keys];
        Dictionary<string, TokenMetadataOnChain> existingRecords = await db.TokenMetadataOnChain
            .Where(t => existingSubjects.Contains(t.Subject))
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Subject);

        foreach ((string? subject, TokenMetadataOnChain? tokenData) in tokensWithMetadata)
        {
            if (existingRecords.TryGetValue(subject, out TokenMetadataOnChain? existing))
            {
                db.TokenMetadataOnChain.Update(tokenData);
                logger.LogInformation("Updated {Subject} - quantity: {OldQty} -> {NewQty}",
                    subject, existing.Quantity, tokenData.Quantity);
            }
            else
            {
                db.TokenMetadataOnChain.Add(tokenData);
                logger.LogInformation("Inserted {Subject} with quantity: {Quantity}", subject, tokenData.Quantity);
            }
        }
        await db.SaveChangesAsync();
    }

    private static TokenMetadataOnChain? ExtractMetadata(
        MetadatumMap rootMap,
        string policyId,
        string assetNameHex,
        long quantity)
    {
        bool isVersion2 = rootMap.Value.Any(kvp => kvp.Key is MetadatumBytes);

        KeyValuePair<TransactionMetadatum, TransactionMetadatum> policyKvp = rootMap.Value.FirstOrDefault(kvp =>
        {
            string? policy = kvp.Key switch
            {
                MetadataText text => text.Value?.ToLowerInvariant(),
                MetadatumBytes bytes => Convert.ToHexString(bytes.Value).ToLowerInvariant(),
                _ => null
            };
            return policy == policyId;
        });

        if (policyKvp.Value is not MetadatumMap policyMap)
            return null;

        KeyValuePair<TransactionMetadatum, TransactionMetadatum> assetKvp = policyMap.Value.FirstOrDefault(kvp =>
        {
            if (isVersion2)
            {
                if (kvp.Key is MetadatumBytes bytes)
                {
                    string hexBytes = Convert.ToHexString(bytes.Value).ToLowerInvariant();
                    return hexBytes == assetNameHex;
                }
            }
            else
            {
                if (kvp.Key is MetadataText textKey && !string.IsNullOrEmpty(textKey.Value))
                {
                    try
                    {
                        string hexFromUtf8 = Convert.ToHexString(Encoding.UTF8.GetBytes(textKey.Value)).ToLowerInvariant();
                        return hexFromUtf8 == assetNameHex;
                    }
                    catch { }
                }
            }

            return false;
        });

        if (assetKvp.Value is not MetadatumMap assetMap)
            return null;

        TokenMetadataOnChain metadata = new(
            Subject: $"{policyId}{assetNameHex}",
            PolicyId: policyId,
            AssetName: assetNameHex,
            Name: "",
            Logo: "",
            Description: "",
            Quantity: quantity,
            Decimals: 0,
            TokenType: TokenType.CIP25
        );

        foreach (KeyValuePair<TransactionMetadatum, TransactionMetadatum> field in assetMap.Value)
        {
            if (field.Key is MetadataText keyText)
            {
                switch (keyText.Value)
                {
                    case "name" when field.Value is MetadataText nameText:
                        metadata = metadata with { Name = nameText.Value ?? "" };
                        break;
                    case "image":
                        string imageUri = field.Value switch
                        {
                            MetadataText imageText => imageText.Value ?? "",
                            MetadatumList imageList => string.Join("", imageList.Value.OfType<MetadataText>().Select(t => t.Value)),
                            _ => ""
                        };
                        if (IsValidUri(imageUri))
                            metadata = metadata with { Logo = imageUri };
                        break;
                    case "description" when field.Value is MetadataText descText:
                        metadata = metadata with { Description = descText.Value ?? "" };
                        break;
                }
            }
        }

        return metadata;
    }

    private static bool IsValidUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return false;

        bool hasValidScheme = ValidSchemes.Any(scheme => uri.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));

        if (!hasValidScheme)
            return false;

        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return DataUriRegex.IsMatch(uri);
        }

        return uri.Length > ValidSchemes.First(s => uri.StartsWith(s, StringComparison.OrdinalIgnoreCase)).Length;
    }

    [GeneratedRegex(@"^data:image\/[a-zA-Z0-9]+(?:\+[a-zA-Z0-9]+)?;base64,", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}