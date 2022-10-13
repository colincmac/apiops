using Azure.ResourceManager.ApiManagement;
using Azure.ResourceManager.ApiManagement.Models;

namespace ApiOps.Core;
public static class ApiManagementHelpers
{
    public static Dictionary<string, Func<ApiManagementServiceResource, string>> ResourceFileProviders = new()
    {
        {"ServicePolicy", (apim) => ""},
        //"NamedValues",
        //"Gateways",
        //"Loggers",
        //"Products",
        //"Apis",
        //"VersionSets",
        //"Diagnostics"
    };

    public const string DefaultGlobalPolicyName = "policy";

    public static ArmResourceEnvelope<ApiManagementNamedValueCreateOrUpdateContent> AsCreateOrUpdateModel(this ApiManagementNamedValueData namedValueData) => new(namedValueData.Name, new()
    {
        IsSecret = namedValueData.IsSecret,
        DisplayName = namedValueData.DisplayName,
        Value = namedValueData.Value,
        KeyVault = namedValueData.KeyVaultDetails,
    });

    public static ArmResourceEnvelope<ApiCreateOrUpdateContent> AsCreateOrUpdateModel(this ApiData apiData) => new(apiData.Name, new()
    {
        ApiRevision = apiData.ApiRevision,
        ApiRevisionDescription = apiData.ApiRevisionDescription,
        ApiType = apiData.ApiType,
        ApiVersion = apiData.ApiVersion,
        ApiVersionDescription = apiData.ApiVersionDescription,
        ApiVersionSetId = apiData.ApiVersionSetId,
        ApiVersionSet = apiData.ApiVersionSet,
        AuthenticationSettings = apiData.AuthenticationSettings,
        Contact = apiData.Contact,
        Description = apiData.Description,
        DisplayName = apiData.DisplayName,
        IsCurrent = apiData.IsCurrent,
        IsSubscriptionRequired = apiData.IsSubscriptionRequired,
        License = apiData.License,
        Path = apiData.Path,
        SourceApiId = apiData.SourceApiId,
        SubscriptionKeyParameterNames = apiData.SubscriptionKeyParameterNames,
        TermsOfServiceUri = apiData.TermsOfServiceUri,
    });

}
