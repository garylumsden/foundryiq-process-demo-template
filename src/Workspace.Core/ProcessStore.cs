using System.Text.Json;

namespace Workspace.Core;

public sealed class ProcessStore(Configuration config)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string path = Path.Combine(config.Root, ".local", "records", config.Profile.Id, config.Process.Id + ".json");

    public async Task<ProcessRecord> SaveAsync(Dictionary<string, JsonElement> values, AnswerResult assessment, CancellationToken ct)
    {
        ProcessValidation.Validate(config.Process, values);
        if (assessment.ProfileDigest != config.Digest
            || assessment.Outcome == "answered" && (assessment.Answer is null || assessment.Verification != "passed" || assessment.Issues.Length > 0)
            || assessment.Outcome != "answered" && assessment.Answer is not null)
            throw new WorkspaceException("assessment_invalid", "Only a server-checked assessment can be recorded.");
        var record = new ProcessRecord(Guid.NewGuid().ToString("N"), config.Profile.Id, config.Digest, config.Profile.Version,
            config.Process, values, assessment, DateTimeOffset.UtcNow);
        await gate.WaitAsync(ct);
        try
        {
            var records = Read();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, records.Append(record).ToArray(), Json.Options, ct);
                await stream.FlushAsync(ct);
            }
            File.Move(temporary, path, overwrite: true);
            return record;
        }
        finally { gate.Release(); }
    }

    public async Task<ProcessRecord[]> ListAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return Read().OrderByDescending(record => record.RecordedAt).ToArray(); }
        finally { gate.Release(); }
    }

    private ProcessRecord[] Read()
    {
        var records = File.Exists(path) ? Json.Read<ProcessRecord[]>(path) : [];
        if (records.Any(record => record.ProfileId != config.Profile.Id || record.Process.Id != config.Process.Id))
            throw new WorkspaceException("records_invalid", "The record file contains a different process or scenario.");
        return records;
    }
}
