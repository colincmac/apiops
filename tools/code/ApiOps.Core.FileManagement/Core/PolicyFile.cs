namespace ApiOps.Core.FileManagement.Core;
public class PolicyFile : ISourceFile
{
    public PolicyFile(Uri fileUri)
    {
        FileUri = fileUri;
    }

    public Uri FileUri { get; }
}
