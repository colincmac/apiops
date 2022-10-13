namespace ApiOps.Core.FileManagement.Core;
public class ResourceDefinitionFile : ISourceFile
{
    public ResourceDefinitionFile(Uri fileUri)
    {
        FileUri = fileUri;
    }

    public Uri FileUri { get; }
}

