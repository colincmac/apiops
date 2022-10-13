using ApiOps.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.ApiManagement;
using Azure.ResourceManager.ApiManagement.Models;
using common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace extractor;

internal class Extractor : BackgroundService
{
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly ILogger logger;
    private readonly NonAuthenticatedHttpClient nonAuthenticatedHttpClient;
    private readonly Func<Uri, CancellationToken, ValueTask<JsonObject?>> tryGetResource;
    private readonly Func<Uri, CancellationToken, ValueTask<JsonObject>> getResource;
    private readonly Func<Uri, CancellationToken, IAsyncEnumerable<JsonObject>> getResources;
    private readonly ServiceDirectory serviceDirectory;
    private readonly ServiceProviderUri serviceProviderUri;
    private readonly ServiceName serviceName;
    private readonly OpenApiSpecification apiSpecification;
    private readonly ConfigurationModel configurationModel;
    private readonly IConfiguration _configuration;
    private readonly ApiManagementServiceResource _apimResource;
    private static readonly JsonSerializerOptions serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Extractor(IHostApplicationLifetime applicationLifetime, ILogger<Extractor> logger, IConfiguration configuration, AzureHttpClient azureHttpClient, NonAuthenticatedHttpClient nonAuthenticatedHttpClient)
    {
        this.applicationLifetime = applicationLifetime;
        this.logger = logger;
        this.nonAuthenticatedHttpClient = nonAuthenticatedHttpClient;
        this.tryGetResource = azureHttpClient.TryGetResourceAsJsonObject;
        this.getResource = azureHttpClient.GetResourceAsJsonObject;
        this.getResources = azureHttpClient.GetResourcesAsJsonObjects;
        this.serviceDirectory = GetServiceDirectory(configuration);
        this.serviceProviderUri = GetServiceProviderUri(configuration, azureHttpClient);
        this.serviceName = GetServiceName(configuration);
        this.apiSpecification = GetApiSpecification(configuration);
        this.configurationModel = configuration.Get<ConfigurationModel>();
        this._configuration = configuration;
        _apimResource = GetApimServiceResource();
    }

    private ApiManagementServiceResource GetApimServiceResource()
    {
        string subscriptionId = _configuration.GetValue("AZURE_SUBSCRIPTION_ID");
        string resourceGroupName = _configuration.GetValue("AZURE_RESOURCE_GROUP_NAME");
        string serviceName = _configuration.TryGetValue("API_MANAGEMENT_SERVICE_NAME") ?? _configuration.GetValue("apimServiceName");

        ArmClient armClient = new(new DefaultAzureCredential());
        return armClient.GetApiManagementServiceResource(ApiManagementServiceResource.CreateResourceIdentifier(subscriptionId, resourceGroupName, serviceName));
    }

    private static ServiceDirectory GetServiceDirectory(IConfiguration configuration) =>
        ServiceDirectory.From(configuration.GetValue("API_MANAGEMENT_SERVICE_OUTPUT_FOLDER_PATH"));

    private static ServiceProviderUri GetServiceProviderUri(IConfiguration configuration, AzureHttpClient azureHttpClient)
    {
        string subscriptionId = configuration.GetValue("AZURE_SUBSCRIPTION_ID");
        string resourceGroupName = configuration.GetValue("AZURE_RESOURCE_GROUP_NAME");

        return ServiceProviderUri.From(azureHttpClient.ResourceManagerEndpoint, subscriptionId, resourceGroupName);
    }

    private static ServiceName GetServiceName(IConfiguration configuration)
    {
        string? serviceName = configuration.TryGetValue("API_MANAGEMENT_SERVICE_NAME") ?? configuration.TryGetValue("apimServiceName");

        return ServiceName.From(serviceName ?? throw new InvalidOperationException("Could not find service name in configuration. Either specify it in key 'apimServiceName' or 'API_MANAGEMENT_SERVICE_NAME'."));
    }

    private static OpenApiSpecification GetApiSpecification(IConfiguration configuration)
    {
        string? configurationFormat = configuration.TryGetValue("API_SPECIFICATION_FORMAT");

        return configurationFormat is null
            ? OpenApiSpecification.V3Yaml
            : configurationFormat switch
            {
                _ when configurationFormat.Equals("JSON", StringComparison.OrdinalIgnoreCase) => OpenApiSpecification.V3Json,
                _ when configurationFormat.Equals("YAML", StringComparison.OrdinalIgnoreCase) => OpenApiSpecification.V3Yaml,
                _ when configurationFormat.Equals("OpenApiV2Json", StringComparison.OrdinalIgnoreCase) => OpenApiSpecification.V2Json,
                _ when configurationFormat.Equals("OpenApiV2Yaml", StringComparison.OrdinalIgnoreCase) => OpenApiSpecification.V2Yaml,
                _ when configurationFormat.Equals("OpenApiV3Json", StringComparison.OrdinalIgnoreCase) => OpenApiSpecification.V3Json,
                _ when configurationFormat.Equals("OpenApiV3Yaml", StringComparison.OrdinalIgnoreCase) => OpenApiSpecification.V3Yaml,
                _ => throw new InvalidOperationException($"API specification format '{configurationFormat}' defined in configuration is not supported.")
            };
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Beginning execution...");

            await Run(cancellationToken);

            logger.LogInformation("Execution complete.");
        }
        catch (OperationCanceledException)
        {
            // Don't throw if operation was canceled
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "");
            Environment.ExitCode = -1;
            throw;
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    private async ValueTask Run(CancellationToken cancellationToken)
    {
        await ExportServicePolicy(cancellationToken);
        await ExportNamedValues(cancellationToken);
        await ExportGateways(cancellationToken);
        await ExportLoggers(cancellationToken);
        await ExportProducts(cancellationToken);
        await ExportApis(cancellationToken);
        await ExportVersionSets(cancellationToken);
        await ExportDiagnostics(cancellationToken);
    }

    private async ValueTask ExportServicePolicy(CancellationToken cancellationToken)
    {
        Azure.Response<ApiManagementPolicyResource> policy = await _apimResource.GetApiManagementPolicyAsync(ApiManagementHelpers.DefaultGlobalPolicyName, PolicyExportFormat.Xml, cancellationToken);

        if (policy.Value.HasData && policy.Value.Data.Value is string policyText)
        {
            logger.LogInformation("Exporting service policy...");

            ServicePolicyFile file = ServicePolicyFile.From(serviceDirectory);
            await file.OverwriteWithText(policyText, cancellationToken);
        }
    }

    public async ValueTask ExportGateways(CancellationToken cancellationToken)
    {
        Azure.AsyncPageable<ApiManagementGatewayResource> gateways = _apimResource.GetApiManagementGateways()
            .GetAllAsync(cancellationToken: cancellationToken);

        await Parallel.ForEachAsync(gateways, cancellationToken, ExportGateway);
    }

    private async ValueTask ExportGateway(ApiManagementGatewayResource gateway, CancellationToken cancellationToken)
    {
        await ExportGatewayInformation(gateway, cancellationToken);
        await ExportGatewayApis(gateway, cancellationToken);
    }

    private async ValueTask ExportGatewayInformation(ApiManagementGatewayResource gateway, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting information for gateway {gatewayName}...", gateway.Data.Name);

        GatewaysDirectory gatewaysDirectory = GatewaysDirectory.From(serviceDirectory);
        GatewayName gatewayName = GatewayName.From(gateway.Data.Name);
        GatewayDirectory gatewayDirectory = GatewayDirectory.From(gatewaysDirectory, gatewayName);
        GatewayInformationFile file = GatewayInformationFile.From(gatewayDirectory);

        // TODO: refine Gateway Data model
        JsonNode json = JsonSerializer.SerializeToNode(gateway.Data, serializerOptions) ?? throw new InvalidOperationException($"Could not serialize Gateway {gateway.Data.Name}.");

        await file.OverwriteWithJson(json, cancellationToken);
    }

    private async ValueTask ExportGatewayApis(ApiManagementGatewayResource gateway, CancellationToken cancellationToken)
    {
        GatewayName gatewayName = GatewayName.From(gateway.Data.Name);
        List<string> apis = await gateway
            .GetGatewayApisByServiceAsync(cancellationToken: cancellationToken)
            .Select(api => api.Name)
            .ToListAsync();
        if (apis.Any())
        {
            // TODO: null value serialization
            JsonNode json = JsonSerializer.SerializeToNode(apis, serializerOptions) ?? new JsonArray();
            logger.LogInformation("Exporting apis for gateway {gatewayName}...", gateway.Data.Name);
            GatewaysDirectory gatewaysDirectory = GatewaysDirectory.From(serviceDirectory);
            GatewayDirectory gatewayDirectory = GatewayDirectory.From(gatewaysDirectory, gatewayName);
            GatewayApisFile file = GatewayApisFile.From(gatewayDirectory);

            await file.OverwriteWithJson(json, cancellationToken);
        }
    }

    private async ValueTask ExportLoggers(CancellationToken cancellationToken)
    {
        Azure.AsyncPageable<ApiManagementLoggerResource> loggers = _apimResource.GetApiManagementLoggers()
            .GetAllAsync(cancellationToken: cancellationToken);

        await Parallel.ForEachAsync(loggers, cancellationToken, ExportLogger);
    }

    private async ValueTask ExportLogger(ApiManagementLoggerResource logger, CancellationToken cancellationToken)
    {
        await ExportLoggerInformation(logger, cancellationToken);
    }

    private async ValueTask ExportLoggerInformation(ApiManagementLoggerResource loggerModel, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting information for logger {loggerName}...", loggerModel.Data.Name);

        LoggersDirectory loggersDirectory = LoggersDirectory.From(serviceDirectory);
        LoggerName loggerName = LoggerName.From(loggerModel.Data.Name);
        LoggerDirectory loggerDirectory = LoggerDirectory.From(loggersDirectory, loggerName);
        LoggerInformationFile file = LoggerInformationFile.From(loggerDirectory);
        JsonNode json = JsonSerializer.SerializeToNode(loggerModel.Data, serializerOptions) ?? throw new InvalidOperationException($"Could not serialize Logger {loggerModel.Data.Name}.");

        await file.OverwriteWithJson(json, cancellationToken);
    }

    private async ValueTask ExportNamedValues(CancellationToken cancellationToken)
    {
        Azure.AsyncPageable<ApiManagementNamedValueResource> namedValues = _apimResource.GetApiManagementNamedValues()
            .GetAllAsync(cancellationToken: cancellationToken);

        await Parallel.ForEachAsync(namedValues, cancellationToken, ExportNamedValue);
    }

    private async ValueTask ExportNamedValue(ApiManagementNamedValueResource namedValue, CancellationToken cancellationToken)
    {
        await ExportNamedValueInformation(namedValue, cancellationToken);
    }

    private async ValueTask ExportNamedValueInformation(ApiManagementNamedValueResource namedValue, CancellationToken cancellationToken)
    {
        // Note: Named Values are one of the few resources that don't use the generated Data model for the CreateOrUpdate operation.
        logger.LogInformation("Exporting information for named value {namedValueName}...", namedValue.Data.Name);

        NamedValuesDirectory namedValuesDirectory = NamedValuesDirectory.From(serviceDirectory);
        NamedValueDisplayName namedValueDisplayName = NamedValueDisplayName.From(namedValue.Data.DisplayName);
        NamedValueDirectory namedValueDirectory = NamedValueDirectory.From(namedValuesDirectory, namedValueDisplayName);
        NamedValueInformationFile file = NamedValueInformationFile.From(namedValueDirectory);
        JsonNode json = JsonSerializer.SerializeToNode(namedValue.Data.AsCreateOrUpdateModel(), serializerOptions) ?? throw new InvalidOperationException($"Could not serialize Named Value {namedValue.Data.Name}.");

        await file.OverwriteWithJson(json, cancellationToken);
    }

    private async ValueTask ExportProducts(CancellationToken cancellationToken)
    {
        Azure.AsyncPageable<ApiManagementProductResource> products = _apimResource.GetApiManagementProducts()
            .GetAllAsync(cancellationToken: cancellationToken);
        await Parallel.ForEachAsync(products, cancellationToken, ExportProduct);
    }

    private async ValueTask ExportProduct(ApiManagementProductResource product, CancellationToken cancellationToken)
    {
        await ExportProductInformation(product, cancellationToken);
        await ExportProductPolicy(product, cancellationToken);
        await ExportProductApis(product, cancellationToken);
    }

    private async ValueTask ExportProductInformation(ApiManagementProductResource product, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting information for product {productName}...", product.Data.Name);

        ProductsDirectory productsDirectory = ProductsDirectory.From(serviceDirectory);
        ProductDisplayName productDisplayName = ProductDisplayName.From(product.Data.DisplayName);
        ProductDirectory productDirectory = ProductDirectory.From(productsDirectory, productDisplayName);
        ProductInformationFile file = ProductInformationFile.From(productDirectory);
        JsonNode json = JsonSerializer.SerializeToNode(product.Data, serializerOptions) ?? throw new InvalidOperationException($"Could not serialize Product {product.Data.Name}.");

        await file.OverwriteWithJson(json, cancellationToken);
    }

    private async ValueTask ExportProductPolicy(ApiManagementProductResource product, CancellationToken cancellationToken)
    {
        Azure.Response<ApiManagementProductPolicyResource> policy = await product.GetApiManagementProductPolicyAsync(ApiManagementHelpers.DefaultGlobalPolicyName, PolicyExportFormat.Xml, cancellationToken);

        if (policy.Value.HasData && policy.Value.Data.Value is string policyText)
        {
            logger.LogInformation("Exporting policy for product {productName}...", product.Data.Name);

            ProductsDirectory productsDirectory = ProductsDirectory.From(serviceDirectory);
            ProductDisplayName productDisplayName = ProductDisplayName.From(product.Data.DisplayName);
            ProductDirectory productDirectory = ProductDirectory.From(productsDirectory, productDisplayName);
            ProductPolicyFile file = ProductPolicyFile.From(productDirectory);

            await file.OverwriteWithText(policyText, cancellationToken);
        }
    }

    private async ValueTask ExportProductApis(ApiManagementProductResource product, CancellationToken cancellationToken)
    {
        ProductName productName = ProductName.From(product.Data.Name);
        List<string> productApis = await product.GetProductApisAsync(cancellationToken: cancellationToken)
            .Select(api => api.Name)
            .ToListAsync();

        if (productApis.Any())
        {
            JsonNode json = JsonSerializer.SerializeToNode(productApis, serializerOptions) ?? new JsonArray();

            logger.LogInformation("Exporting apis for product {productName}...", product.Data.Name);
            ProductsDirectory productsDirectory = ProductsDirectory.From(serviceDirectory);
            ProductDisplayName productDisplayName = ProductDisplayName.From(product.Data.DisplayName);
            ProductDirectory productDirectory = ProductDirectory.From(productsDirectory, productDisplayName);
            ProductApisFile file = ProductApisFile.From(productDirectory);

            await file.OverwriteWithJson(json, cancellationToken);
        }
    }

    private async ValueTask ExportDiagnostics(CancellationToken cancellationToken)
    {
        Azure.AsyncPageable<ApiManagementDiagnosticResource> diagnostics = _apimResource.GetApiManagementDiagnostics()
            .GetAllAsync(cancellationToken: cancellationToken);

        await Parallel.ForEachAsync(diagnostics, cancellationToken, ExportDiagnostic);
    }

    private async ValueTask ExportDiagnostic(ApiManagementDiagnosticResource diagnostic, CancellationToken cancellationToken)
    {
        await ExportDiagnosticInformation(diagnostic, cancellationToken);
    }

    private async ValueTask ExportDiagnosticInformation(ApiManagementDiagnosticResource diagnostic, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting information for diagnostic {diagnosticName}...", diagnostic.Data.Name);

        DiagnosticsDirectory diagnosticsDirectory = DiagnosticsDirectory.From(serviceDirectory);
        DiagnosticName diagnosticName = DiagnosticName.From(diagnostic.Data.Name);
        DiagnosticDirectory diagnosticDirectory = DiagnosticDirectory.From(diagnosticsDirectory, diagnosticName);
        DiagnosticInformationFile file = DiagnosticInformationFile.From(diagnosticDirectory);
        JsonNode json = JsonSerializer.SerializeToNode(diagnostic.Data, serializerOptions) ?? throw new InvalidOperationException($"Could not serialize Diagnostic {diagnostic.Data.Name}.");

        await file.OverwriteWithJson(json, cancellationToken);
    }

    private async ValueTask ExportVersionSets(CancellationToken cancellationToken)
    {
        Azure.AsyncPageable<ApiVersionSetResource> versionSets = _apimResource.GetApiVersionSets()
                    .GetAllAsync(cancellationToken: cancellationToken);

        await Parallel.ForEachAsync(versionSets, cancellationToken, ExportVersionSet);
    }

    private async ValueTask ExportVersionSet(ApiVersionSetResource apiVersionSet, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting information for version set {versionSetName}...", apiVersionSet.Data.Name);

        ApisDirectory apisDirectory = ApisDirectory.From(serviceDirectory);
        ApiDisplayName apiDisplayName = ApiDisplayName.From(apiVersionSet.Data.DisplayName);

        ApiVersionSetDirectory apiDirectory = ApiVersionSetDirectory.From(apisDirectory, apiDisplayName);

        ApiVersionSetInformationFile file = ApiVersionSetInformationFile.From(apiDirectory);
        JsonNode json = JsonSerializer.SerializeToNode(apiVersionSet.Data, serializerOptions) ?? throw new InvalidOperationException($"Could not serialize ApiVersionSet {apiVersionSet.Data.Name}.");
        await file.OverwriteWithJson(json, cancellationToken);
    }

    private async ValueTask ExportApis(CancellationToken cancellationToken)
    {
        Azure.AsyncPageable<ApiResource> apis = _apimResource.GetApis()
            .GetAllAsync(cancellationToken: cancellationToken);

        await Parallel.ForEachAsync(apis, cancellationToken, ExportApi);
    }

    private async ValueTask ExportApi(ApiResource api, CancellationToken cancellationToken)
    {
        await ExportApiInformation(api, cancellationToken);
        await ExportApiPolicy(api, cancellationToken);
        await ExportApiSpecification(api, cancellationToken);
        await ExportApiDiagnostics(api, cancellationToken);
        await ExportApiOperations(api, cancellationToken);
    }

    private async ValueTask ExportApiInformation(ApiResource api, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting information for api {apiName}...", api.Data.Name);

        ApisDirectory apisDirectory = ApisDirectory.From(serviceDirectory);
        ApiDisplayName apiDisplayName = ApiDisplayName.From(api.Data.DisplayName);
        ApiVersion apiVersion = ApiVersion.From(api.Data.ApiVersion);
        ApiRevision apiRevision = ApiRevision.From(api.Data.ApiRevision);

        ApiDirectory apiDirectory = ApiDirectory.From(apisDirectory, apiDisplayName, apiVersion, apiRevision);
        ApiInformationFile file = ApiInformationFile.From(apiDirectory);
        JsonNode json = JsonSerializer.SerializeToNode(api.Data.AsCreateOrUpdateModel(), serializerOptions) ?? throw new InvalidOperationException($"Could not serialize Api {api.Data.Name}.");

        await file.OverwriteWithJson(json, cancellationToken);
    }

    private async ValueTask ExportApiPolicy(ApiResource api, CancellationToken cancellationToken)
    {
        Azure.Response<ApiPolicyResource> policy = await api.GetApiPolicyAsync(ApiManagementHelpers.DefaultGlobalPolicyName, PolicyExportFormat.Xml, cancellationToken);

        if (policy.Value.HasData && policy.Value.Data.Value is string policyText)
        {
            logger.LogInformation("Exporting policy for api {apiName}...", api.Data.Name);

            ApisDirectory apisDirectory = ApisDirectory.From(serviceDirectory);
            ApiDisplayName apiDisplayName = ApiDisplayName.From(api.Data.DisplayName);
            ApiVersion apiVersion = ApiVersion.From(api.Data.ApiVersion);
            ApiRevision apiRevision = ApiRevision.From(api.Data.ApiRevision);
            ApiDirectory apiDirectory = ApiDirectory.From(apisDirectory, apiDisplayName, apiVersion, apiRevision);
            ApiPolicyFile file = ApiPolicyFile.From(apiDirectory);

            await file.OverwriteWithText(policyText, cancellationToken);
        }
    }

    private async ValueTask ExportApiSpecification(ApiResource api, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting specification for api {apiName}...", api.Data.Name);
        ApiSchemaResource? schema = await api.GetApiSchemas().FirstOrDefaultAsync(cancellationToken);
        if (schema is null) throw new InvalidOperationException($"Could not fetch specification for api {api.Data.DisplayName}.");
        ApisDirectory apisDirectory = ApisDirectory.From(serviceDirectory);
        ApiDisplayName apiDisplayName = ApiDisplayName.From(api.Data.DisplayName);
        ApiVersion apiVersion = ApiVersion.From(api.Data.ApiVersion);
        ApiRevision apiRevision = ApiRevision.From(api.Data.ApiRevision);
        ApiDirectory apiDirectory = ApiDirectory.From(apisDirectory, apiDisplayName, apiVersion, apiRevision);
        ApiSpecificationFile file = ApiSpecificationFile.From(apiDirectory, apiSpecification);

        //ApiName apiName = ApiName.From(api.Data.Name);
        //Func<Uri, CancellationToken, ValueTask<System.IO.Stream>> downloader = nonAuthenticatedHttpClient.GetSuccessfulResponseStream;
        //using System.IO.Stream specificationStream = await ApiSpecification.Get(getResource, downloader, serviceProviderUri, serviceName, apiName, apiSpecification, cancellationToken);
        using System.IO.Stream specStream = schema.Data.Components.ToStream();

        await file.OverwriteWithStream(specStream, cancellationToken);
    }

    private async ValueTask ExportApiDiagnostics(ApiResource api, CancellationToken cancellationToken)
    {
        Azure.AsyncPageable<ApiDiagnosticResource> diagnostics = api.GetApiDiagnostics()
            .GetAllAsync(cancellationToken: cancellationToken);

        await Parallel.ForEachAsync(diagnostics,
                                    cancellationToken,
                                    (diagnostic, cancellationToken) => ExportApiDiagnostic(api, diagnostic, cancellationToken));
    }

    private async ValueTask ExportApiDiagnostic(ApiResource api, ApiDiagnosticResource diagnostic, CancellationToken cancellationToken)
    {
        logger.LogInformation("Exporting diagnostic {apiDiagnostic}for api {apiName}...", diagnostic.Data.Name, api.Data.Name);

        ApisDirectory apisDirectory = ApisDirectory.From(serviceDirectory);
        ApiDisplayName apiDisplayName = ApiDisplayName.From(api.Data.DisplayName);
        ApiVersion apiVersion = ApiVersion.From(api.Data.ApiVersion);
        ApiRevision apiRevision = ApiRevision.From(api.Data.ApiRevision);
        ApiDirectory apiDirectory = ApiDirectory.From(apisDirectory, apiDisplayName, apiVersion, apiRevision);
        ApiDiagnosticsDirectory apiDiagnosticsDirectory = ApiDiagnosticsDirectory.From(apiDirectory);
        ApiDiagnosticName apiDiagnosticName = ApiDiagnosticName.From(diagnostic.Data.Name);
        ApiDiagnosticDirectory apiDiagnosticDirectory = ApiDiagnosticDirectory.From(apiDiagnosticsDirectory, apiDiagnosticName);
        ApiDiagnosticInformationFile file = ApiDiagnosticInformationFile.From(apiDiagnosticDirectory);
        JsonNode json = JsonSerializer.SerializeToNode(diagnostic.Data, serializerOptions) ?? throw new InvalidOperationException($"Could not serialize Api Diagnostic {diagnostic.Data.Name}.");

        await file.OverwriteWithJson(json, cancellationToken);
    }

    private async ValueTask ExportApiOperations(ApiResource api, CancellationToken cancellationToken)
    {
        Azure.AsyncPageable<ApiOperationResource> apiOperations = api.GetApiOperations()
            .GetAllAsync(cancellationToken: cancellationToken);

        await Parallel.ForEachAsync(apiOperations,
                                    cancellationToken,
                                    (apiOperation, cancellationToken) => ExportApiOperation(api, apiOperation, cancellationToken));
    }

    private async ValueTask ExportApiOperation(ApiResource api, ApiOperationResource apiOperation, CancellationToken cancellationToken)
    {
        await ExportApiOperationPolicy(api, apiOperation, cancellationToken);
    }

    private async ValueTask ExportApiOperationPolicy(ApiResource api, ApiOperationResource apiOperation, CancellationToken cancellationToken)
    {
        Azure.Response<ApiPolicyResource> policy = await api.GetApiPolicyAsync(ApiManagementHelpers.DefaultGlobalPolicyName, PolicyExportFormat.Xml, cancellationToken);

        if (policy.Value.HasData && policy.Value.Data.Value is string policyText)
        {
            logger.LogInformation("Exporting policy for apiOperation {apiOperationName} in api {apiName}...", apiOperation.Data.Name, api.Data.Name);

            ApisDirectory apisDirectory = ApisDirectory.From(serviceDirectory);
            ApiDisplayName apiDisplayName = ApiDisplayName.From(api.Data.DisplayName);
            ApiVersion apiVersion = ApiVersion.From(api.Data.ApiVersion);
            ApiRevision apiRevision = ApiRevision.From(api.Data.ApiRevision);
            ApiDirectory apiDirectory = ApiDirectory.From(apisDirectory, apiDisplayName, apiVersion, apiRevision);
            ApiOperationsDirectory apiOperationsDirectory = ApiOperationsDirectory.From(apiDirectory);
            ApiOperationDisplayName apiOperationDisplayName = ApiOperationDisplayName.From(apiOperation.Data.DisplayName);
            ApiOperationDirectory apiOperationDirectory = ApiOperationDirectory.From(apiOperationsDirectory, apiOperationDisplayName);
            ApiOperationPolicyFile file = ApiOperationPolicyFile.From(apiOperationDirectory);

            await file.OverwriteWithText(policyText, cancellationToken);
        }
    }
}