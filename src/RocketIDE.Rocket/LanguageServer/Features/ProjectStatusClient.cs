using System.Text.Json;
using RocketIDE.Rocket.LanguageServer.LspDtos;

namespace RocketIDE.Rocket.LanguageServer.Features;

public sealed record RocketProjectStatusRequestResult(bool IsSupported, RocketProjectStatus? Status);

public sealed class ProjectStatusClient(IRocketLanguageClient client)
{
    public async Task<RocketProjectStatusRequestResult> RequestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.RequestAsync<JsonElement>("rocket/projectStatus", new { }, cancellationToken)
                .ConfigureAwait(false);
            if (response.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return new RocketProjectStatusRequestResult(true, null);
            }
            if (response.ValueKind != JsonValueKind.Object)
            {
                throw new LspProtocolException("rocket/projectStatus returned an invalid result shape.");
            }
            var status = response.Deserialize<RocketProjectStatus>(LspJson.Options)
                ?? throw new LspProtocolException("rocket/projectStatus returned an empty result.");
            return new RocketProjectStatusRequestResult(true, status);
        }
        catch (JsonRpcResponseException exception) when (exception.Code == -32601)
        {
            return new RocketProjectStatusRequestResult(false, null);
        }
        catch (JsonException exception)
        {
            throw new LspProtocolException("rocket/projectStatus returned malformed JSON.", exception);
        }
    }
}
