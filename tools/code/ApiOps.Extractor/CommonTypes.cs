namespace ApiOps.Extractor;

/// <summary>
/// Certain HTTP calls require an HTTP client that is not authenticated. For instance, calls to download a blob with a SAS token.
/// </summary>
internal class NonAuthenticatedHttpClient
{
    private readonly HttpClient client;

    public NonAuthenticatedHttpClient(HttpClient client)
    {
        if (client.DefaultRequestHeaders.Authorization is not null)
        {
            throw new InvalidOperationException("Client cannot have authorization headers.");
        }

        this.client = client;
    }

    public async ValueTask<Stream> GetSuccessfulResponseStream(Uri uri, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await client.GetAsync(uri, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStreamAsync(cancellationToken);
        }
        else
        {
            string responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            string errorMessage = $"Response was unsuccessful. Status code is {response.StatusCode}. Response content was {responseContent}.";
            throw new InvalidOperationException(errorMessage);
        }
    }
}