using Azure;

namespace ApiOps.Core;
public record ArmResourceEnvelope<TPropertyBag>(string Name, TPropertyBag Properties)
{
    public (WaitUntil, string, TPropertyBag) AsArmRequestParams(WaitUntil waitUntil = WaitUntil.Completed) => (waitUntil, Name, Properties);
}

