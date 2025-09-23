using Argus.Sync.Reducers;
using Chrysalis.Cbor.Extensions;
using Chrysalis.Cbor.Extensions.Cardano.Core;
using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Extensions.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Cbor.Types.Plutus;
using Chrysalis.Wallet.Utils;
using Comp.Models;
using Comp.Models.Entity;
using Microsoft.EntityFrameworkCore;

namespace Comp.Sync.Reducers;

public class CIP68Reducer(
    IDbContextFactory<MetadataDbContext> dbContextFactory,
    ILogger<CIP68Reducer> logger
) : IReducer<TokenMetadataOnChain>
{
    private const string REFERENCE_PREFIX = "000643b0";
    private static readonly Dictionary<string, string> UserTokenPrefixes = new()
    {
        { "FT", "000de140" },
        { "NFT", "0014df10" },
        { "RFT", "001bc280" }
    };

    public async Task RollBackwardAsync(ulong slot)
    {
        await Task.CompletedTask;
    }

    public async Task RollForwardAsync(Block block)
    {
        List<TransactionBody> txBodies = [.. block.TransactionBodies()];
        List<TransactionWitnessSet> witnessSets = [.. block.TransactionWitnessSets()];

        Dictionary<string, (string policyId, string baseName, byte[] datumBytes)> referenceTokens = ExtractAllReferenceTokens(txBodies, witnessSets, logger);
        if (referenceTokens.Count == 0)
            return;

        Dictionary<string, TokenMetadataOnChain> tokensToProcess = await ProcessReferenceTokensAsync(referenceTokens, txBodies);
        if (tokensToProcess.Count > 0)
            await SaveTokensAsync(tokensToProcess);
    }

    private static Dictionary<string, (string policyId, string baseName, byte[] datumBytes)> ExtractAllReferenceTokens(
        List<TransactionBody> txBodies,
        List<TransactionWitnessSet> witnessSets,
        ILogger<CIP68Reducer> logger)
    {
        Dictionary<string, (string policyId, string baseName, byte[] datumBytes)> referenceTokens = [];

        for (int i = 0; i < txBodies.Count; i++)
        {
            List<TransactionOutput>? outputs = txBodies[i].Outputs()?.ToList();
            if (outputs == null || outputs.Count == 0)
                continue;

            TransactionWitnessSet? witnessSet = i < witnessSets.Count ? witnessSets[i] : null;
            ExtractReferenceTokensFromOutputs(outputs, referenceTokens, witnessSet, logger);
        }

        return referenceTokens;
    }

    private static void ExtractReferenceTokensFromOutputs(
        List<TransactionOutput> outputs,
        Dictionary<string, (string policyId, string baseName, byte[] datumBytes)> referenceTokens,
        TransactionWitnessSet? witnessSet,
        ILogger<CIP68Reducer> logger)
    {
        foreach (TransactionOutput output in outputs)
        {
            Dictionary<byte[], TokenBundleOutput>? multiAsset = output.Amount()?.MultiAsset();
            if (multiAsset == null)
                continue;

            foreach (KeyValuePair<byte[], TokenBundleOutput> policy in multiAsset)
            {
                string policyId = Convert.ToHexString(policy.Key).ToLowerInvariant();
                ProcessPolicyAssets(policy.Value.Value, policyId, output, referenceTokens, witnessSet, logger);
            }
        }
    }

    private static void ProcessPolicyAssets(
        Dictionary<byte[], ulong> assets,
        string policyId,
        TransactionOutput output,
        Dictionary<string, (string policyId, string baseName, byte[] datumBytes)> referenceTokens,
        TransactionWitnessSet? witnessSet,
        ILogger<CIP68Reducer> logger)
    {
        foreach (KeyValuePair<byte[], ulong> asset in assets)
        {
            if (asset.Value == 0)
                continue;

            string assetNameHex = Convert.ToHexString(asset.Key).ToLowerInvariant();
            if (!assetNameHex.StartsWith(REFERENCE_PREFIX))
                continue;

            string baseName = assetNameHex[8..];
            string referenceSubject = $"{policyId}{assetNameHex}";

            if (referenceTokens.ContainsKey(referenceSubject))
                continue;

            byte[]? datumBytes = ExtractDatum(output, witnessSet);
            if (datumBytes == null)
            {
                logger.LogDebug("No datum found for reference token {Subject}", referenceSubject);
                continue;
            }

            referenceTokens[referenceSubject] = (policyId, baseName, datumBytes);
        }
    }

    private async Task<Dictionary<string, TokenMetadataOnChain>> ProcessReferenceTokensAsync(
        Dictionary<string, (string policyId, string baseName, byte[] datumBytes)> referenceTokens,
        List<TransactionBody> txBodies)
    {
        Dictionary<string, long> mintedUserTokens = ExtractMintedUserTokens(txBodies);
        Dictionary<string, TokenMetadataOnChain> tokensToProcess = [];

        foreach ((string _, (string? policyId, string? baseName, byte[]? datumBytes)) in referenceTokens)
        {
            string? userTokenSubject = await DetermineUserTokenSubjectAsync(baseName, policyId, mintedUserTokens);
            if (userTokenSubject == null)
                continue;

            long quantity = await GetTokenQuantityAsync(userTokenSubject, mintedUserTokens);
            string assetName = userTokenSubject[policyId.Length..];
            (string? name, string? image, string? description, int? decimals) = ExtractCIP68Metadata(datumBytes);

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(image))
            {
                logger.LogWarning("Skipping {Subject} - CIP-68 requires name and image fields", userTokenSubject);
                continue;
            }

            tokensToProcess[userTokenSubject] = new TokenMetadataOnChain(
                Subject: userTokenSubject,
                PolicyId: policyId,
                AssetName: assetName,
                Name: name,
                Logo: image,
                Description: description,
                Quantity: quantity,
                Decimals: decimals ?? 0,
                TokenType: TokenType.CIP68
            );

            logger.LogDebug("Processed CIP-68 token: {Subject} (qty: {Quantity})", userTokenSubject, quantity);
        }

        return tokensToProcess;
    }

    private static Dictionary<string, long> ExtractMintedUserTokens(List<TransactionBody> txBodies)
    {
        Dictionary<string, long> mintedUserTokens = [];

        foreach (TransactionBody tx in txBodies)
        {
            Dictionary<byte[], TokenBundleMint>? mint = tx.Mint();
            if (mint == null)
                continue;

            foreach (KeyValuePair<byte[], TokenBundleMint> policyEntry in mint)
            {
                string policyId = Convert.ToHexString(policyEntry.Key).ToLowerInvariant();
                foreach (KeyValuePair<byte[], long> asset in policyEntry.Value.Value)
                {
                    if (asset.Value <= 0)
                        continue;

                    string assetNameHex = Convert.ToHexString(asset.Key).ToLowerInvariant();
                    if (UserTokenPrefixes.Values.Any(assetNameHex.StartsWith))
                    {
                        string subject = $"{policyId}{assetNameHex}";
                        mintedUserTokens[subject] = asset.Value;
                    }
                }
            }
        }

        return mintedUserTokens;
    }

    private async Task<long> GetTokenQuantityAsync(string userTokenSubject, Dictionary<string, long> mintedUserTokens)
    {
        long quantity = mintedUserTokens.GetValueOrDefault(userTokenSubject, 0);
        if (quantity > 0)
            return quantity;

        await using MetadataDbContext db = await dbContextFactory.CreateDbContextAsync();
        TokenMetadataOnChain? existing = await db.TokenMetadataOnChain
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Subject == userTokenSubject);
        return existing?.Quantity ?? 0;
    }

    private async Task<string?> DetermineUserTokenSubjectAsync(
        string baseName,
        string policyId,
        Dictionary<string, long> mintedUserTokens)
    {
        foreach ((string _, string? prefix) in UserTokenPrefixes)
        {
            string subject = $"{policyId}{prefix}{baseName}";
            if (mintedUserTokens.ContainsKey(subject))
                return subject;
        }

        await using MetadataDbContext db = await dbContextFactory.CreateDbContextAsync();
        foreach ((string _, string? prefix) in UserTokenPrefixes)
        {
            string subject = $"{policyId}{prefix}{baseName}";
            bool exists = await db.TokenMetadataOnChain.AnyAsync(t => t.Subject == subject);
            if (exists)
                return subject;
        }

        return null;
    }

    private (string name, string image, string description, int? decimals) ExtractCIP68Metadata(byte[] datumBytes)
    {
        try
        {
            Cip68<PlutusData> datum = CborSerializer.Deserialize<Cip68<PlutusData>>(datumBytes);
            if (datum?.Metadata == null)
                return ("", "", "", null);

            string? name = null, image = null, description = null;
            int? decimals = null;

            if (datum.Metadata is PlutusMap map)
            {
                foreach (KeyValuePair<PlutusData, PlutusData> kvp in map.PlutusData)
                {
                    if (kvp.Key is PlutusBoundedBytes keyBytes)
                    {
                        string key = System.Text.Encoding.UTF8.GetString(keyBytes.Value);

                        switch (key)
                        {
                            case "name":
                                if (kvp.Value is PlutusBoundedBytes nameBytes)
                                {
                                    name = System.Text.Encoding.UTF8.GetString(nameBytes.Value);
                                }
                                break;
                            case "image":
                                if (kvp.Value is PlutusBoundedBytes imageBytes)
                                {
                                    image = System.Text.Encoding.UTF8.GetString(imageBytes.Value);
                                }
                                break;
                            case "description":
                                if (kvp.Value is PlutusBoundedBytes descBytes)
                                {
                                    description = System.Text.Encoding.UTF8.GetString(descBytes.Value);
                                }
                                break;
                            case "decimals":
                                if (kvp.Value is PlutusInt64 decInt)
                                {
                                    decimals = (int)decInt.Value;
                                }
                                else if (kvp.Value is PlutusUint64 decUint)
                                {
                                    decimals = (int)decUint.Value;
                                }
                                break;
                        }
                    }
                }
            }
            return (name ?? "", image ?? "", description ?? "", decimals);
        }
        catch
        {
            return ("", "", "", null);
        }
    }

    private static byte[]? ExtractDatum(TransactionOutput output, TransactionWitnessSet? witnessSet)
    {
        DatumOption? datumOption = output.DatumOption();

        if (datumOption is InlineDatumOption inlineDatum)
        {
            byte[] rawBytes = inlineDatum.Data.Value;
            if (rawBytes.Length >= 2 && rawBytes[0] == 0xD8 && rawBytes[1] == 0x18)
            {
                return inlineDatum.Data.GetValue();
            }
            return rawBytes;
        }

        if (datumOption != null)
            return null;

        byte[]? datumHash = output.DatumHash();
        if (datumHash == null || datumHash.Length == 0 || witnessSet == null)
            return null;

        IEnumerable<PlutusData> plutusDataSet = witnessSet.PlutusDataSet() ?? [];
        return ResolveDatumFromWitnessSet(datumHash, plutusDataSet);
    }

    private static byte[]? ResolveDatumFromWitnessSet(byte[] datumHash, IEnumerable<PlutusData> plutusDataSet)
    {
        foreach (PlutusData plutusData in plutusDataSet)
        {
            byte[] datumBytes = plutusData.Raw();
            byte[] calculatedHash = HashUtil.Blake2b256(datumBytes);

            if (calculatedHash.SequenceEqual(datumHash))
            {
                return datumBytes;
            }
        }
        return null;
    }

    private async Task SaveTokensAsync(Dictionary<string, TokenMetadataOnChain> tokensToProcess)
    {
        await using MetadataDbContext db = await dbContextFactory.CreateDbContextAsync();

        List<string> subjects = [.. tokensToProcess.Keys];
        Dictionary<string, TokenMetadataOnChain> existingRecords = await db.TokenMetadataOnChain
            .Where(t => subjects.Contains(t.Subject))
            .AsNoTracking()
            .ToDictionaryAsync(t => t.Subject);

        foreach ((string? subject, TokenMetadataOnChain? tokenData) in tokensToProcess)
        {
            if (existingRecords.ContainsKey(subject))
            {
                db.TokenMetadataOnChain.Update(tokenData);
                logger.LogInformation("Updated CIP-68 token {Subject} with quantity {Quantity}",
                    subject, tokenData.Quantity);
            }
            else
            {
                db.TokenMetadataOnChain.Add(tokenData);
                logger.LogInformation("Inserted CIP-68 token {Subject} with quantity {Quantity}",
                    subject, tokenData.Quantity);
            }
        }

        await db.SaveChangesAsync();
    }
}