namespace ApiOps.Core.FileManagement.Core;
public class ApiSpecificationFile : ISourceFile
{
    public ApiSpecificationFile(Uri fileUri)
    {
        FileUri = fileUri;
    }

    public Uri FileUri { get; }
}
