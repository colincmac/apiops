namespace ApiOps.Core.FileManagement.Core;
public record UriDirectorySchema(Uri RootDirectory)
{
    public Uri ApisDirectoryUri = new(RootDirectory, "apis");
    public Uri NamedValuesDirectoryUri = new(RootDirectory, "named values");
    public Uri ProductsDirectoryUri = new(RootDirectory, "products");

    public Uri GetApiUri(string apiDisplayName) => new(ApisDirectoryUri, apiDisplayName);
    public Uri GetApiVersionDirectoryUri(string apiDisplayName, string version) => new(GetApiUri(apiDisplayName), version);
    public Uri GetApiVersionRevisionDirectoryUri(string apiDisplayName, string version, string revision) => new(GetApiVersionDirectoryUri(apiDisplayName, version), revision);
    public Uri GetProductDirectoryUri(string productName) => new(ProductsDirectoryUri, productName);
    public Uri GetNamedValueDirectoryUri(string namedValueName) => new(NamedValuesDirectoryUri, namedValueName);
}
