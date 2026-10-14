using Workspace.Core;

namespace Workspace.Tools;

public static class EvaluationChecks
{
    public static EvaluationCase[] Read(Configuration config)
    {
        var path = Path.Combine(config.Root, "evals", config.Profile.Id, "cases.json");
        if (!File.Exists(path)) throw new WorkspaceException("cases_missing", "Create evals/<profile-id>/cases.json before validating acceptance.");
        var cases = Json.Read<EvaluationCase[]>(path);
        Validate(config, cases);
        return cases;
    }

    public static void Validate(Configuration config, EvaluationCase[] cases)
    {
        if (cases.Length == 0 || cases.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id))
            || cases.Select(item => item.Id).Distinct().Count() != cases.Length)
            throw new WorkspaceException("cases_invalid", "cases.json: provide unique nonempty cases.");
        for (var index = 0; index < cases.Length; index++)
        {
            var item = cases[index];
            if (string.IsNullOrWhiteSpace(item.Question) || item.Question.Length > 4000
                || item.ExpectedOutcome is not ("answered" or "insufficient_evidence" or "clarification_required" or "out_of_scope")
                || item.MinimumRecall is < 0.9 or > 1 || !double.IsFinite(item.MinimumRecall)
                || item.ExpectedSources.Distinct().Count() != item.ExpectedSources.Length
                || item.ExpectedOutcome == "answered" && item.ExpectedSources.Length == 0
                || item.ExpectedSources.Any(id => !config.Sources.Documents.Any(source => source.Id == id
                    && source.Eligible(DateOnly.FromDateTime(DateTime.UtcNow)))))
                throw new WorkspaceException("cases_invalid", $"cases.json [{index}]: check the question, expected outcome, eligible source IDs, and recall threshold.");
            if (item.Images.Length > 0 && config.Models.Answers.Any(model => !model.Capabilities.Images))
                throw new WorkspaceException("cases_incompatible", $"cases.json [{index}]: image cases require image input support in every selected answer model.");
            ImageValidation.Normalize(item.Images.Select(config.ReadImage).ToArray());
        }
    }
}
