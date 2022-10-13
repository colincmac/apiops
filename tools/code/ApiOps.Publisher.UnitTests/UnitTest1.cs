using common;
using publisher;
using System.Collections.Immutable;

namespace ApiOps.Publisher.UnitTests;

public class Tests
{
    private readonly string _gitDiff1 = File.ReadAllText("./resources/git-diff-1.txt");
    private readonly ServiceDirectory _serviceDirectory = ServiceDirectory.From("resources");
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task Test1()
    {

        IAsyncEnumerable<(Action action, IEnumerable<FileRecord> fileRecords)> files =
            from grouping in GetFilesFromCommit()
            let action = grouping.Key == CommitStatus.Delete ? Action.Delete : Action.Put
            let fileRecords = grouping.Choose(TryClassifyFile)
            select (action, fileRecords);
        List<(Action action, IEnumerable<FileRecord> fileRecords)> fileList = await files.ToListAsync();
        //List<(Action, IEnumerable<FileRecord>)> fileL2 = new()
        //{
        //    (Action.Put, TryClassifyFile(File.))
        //};

        ImmutableDictionary<Action, ImmutableList<FileRecord>> dict = fileList.ToImmutableDictionary(pair => pair.action, pair => pair.fileRecords.ToImmutableList());

        Assert.Pass();
    }

    private async IAsyncEnumerable<IGrouping<CommitStatus, FileInfo>> GetFilesFromCommit()
    {
        string diffTreeOutput = await Task.FromResult(_gitDiff1);

        foreach (IGrouping<CommitStatus, FileInfo> grouping in Git.ParseDiffTreeOutput(diffTreeOutput, new DirectoryInfo("./resources")))
        {
            yield return grouping;
        }
    }

    //public static IEnumerable<IGrouping<CommitStatus, FileInfo>> ParseDiffTreeOutput(string output, DirectoryInfo baseDirectory)
    //{
    //    var getFileFromOutputLine = (string outputLine) => new FileInfo(Path.Combine(baseDirectory.FullName, outputLine[1..].Trim()));

    //    return
    //        from outputLine in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
    //        let commitStatus = TryGetCommitStatusFromOutputLine(outputLine)
    //        where commitStatus is not null
    //        let nonNullCommitStatus = commitStatus ?? throw new NullReferenceException() // Shouldn't be null here, adding to satisfy nullable compiler check
    //        let file = getFileFromOutputLine(outputLine)
    //        group file by nonNullCommitStatus;
    //}

    private FileRecord? TryClassifyFile(FileInfo file) =>
    GatewayInformationFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? LoggerInformationFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ServicePolicyFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? NamedValueInformationFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ProductInformationFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? GatewayApisFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ProductPolicyFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ProductApisFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? DiagnosticInformationFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ApiVersionSetInformationFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ApiInformationFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ApiSpecificationFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ApiDiagnosticInformationFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ApiPolicyFile.TryFrom(_serviceDirectory, file) as FileRecord
    ?? ApiOperationPolicyFile.TryFrom(_serviceDirectory, file) as FileRecord;


    private enum Action
    {
        Put,
        Delete
    }
}