using System.Text.RegularExpressions;

namespace Workspace.Core;

public static partial class AnswerValidation
{
    public static AgentAnswer NormalizeProviderReferences(AgentAnswer answer, Evidence[] sources)
    {
        string Resolve(string reference)
        {
            if (sources.Any(source => source.RefId == reference)) return reference;
            var chunks = sources.Where(source => source.ChunkId == reference).ToArray();
            if (chunks.Length == 1) return chunks[0].RefId;
            if (chunks.Length > 1) return reference;
            var label = ProviderCitationLabel().Match(reference);
            if (!label.Success || !int.TryParse(label.Groups[1].Value, out var index)
                || index < 0 || index >= sources.Length) return reference;
            return sources[index].RefId;
        }
        return answer with
        {
            Overview = answer.Overview.Select(statement => statement with
                { References = statement.References.Select(Resolve).ToArray() }).ToArray(),
            Summaries = answer.Summaries.Select(summary => summary with
                { References = summary.References.Select(Resolve).ToArray() }).ToArray(),
            Quotations = answer.Quotations.Select(quotation => quotation with
                { Reference = Resolve(quotation.Reference) }).ToArray()
        };
    }

    public static AnswerIssue[] Inspect(AgentAnswer answer, Evidence[] sources, int imageCount)
    {
        var issues = new List<AnswerIssue>();
        void Add(string code, string location, string message) => issues.Add(new(code, location, message));
        if (answer.Overview is null || answer.Summaries is null || answer.Quotations is null || answer.Images is null
            || answer.Limitations is null)
        {
            Add("answer_contract_invalid", "answer", "The agent returned an incomplete answer.");
            return issues.ToArray();
        }
        if (answer.Limitations.Length == 0 || answer.Limitations.Any(string.IsNullOrWhiteSpace))
            Add("limitations_missing", "limitations", "The answer does not state its limits or missing context.");
        if (answer.Outcome is not ("answered" or "insufficient_evidence" or "clarification_required" or "out_of_scope"))
            Add("answer_outcome_invalid", "answer", "The agent returned an unknown outcome.");
        if (answer.Outcome != "answered" && (answer.Overview.Length > 0 || answer.Summaries.Length > 0 || answer.Quotations.Length > 0))
            Add("abstention_invalid", "answer", "The answer includes factual claims despite its abstention outcome.");
        if (answer.Outcome == "answered" && sources.Length == 0)
            Add("answered_without_evidence", "answer", "The answer has no verified source evidence.");
        if (answer.Outcome == "answered" && answer.Overview.Length == 0)
            Add("answered_without_overview", "overview", "The answer has no overview statements.");
        var refs = sources.ToDictionary(s => s.RefId);
        void Check(string text, string[] references, string location)
        {
            if (string.IsNullOrWhiteSpace(text))
                Add("statement_empty", location, "The statement is empty.");
            if (references is null || references.Length == 0)
                Add("citation_missing", location, "The statement has no citation.");
            else
                foreach (var reference in references.Where(reference => !refs.ContainsKey(reference)).Distinct())
                    Add("citation_unknown", location, $"The citation '{reference}' does not match returned evidence.");
        }
        for (var index = 0; index < answer.Overview.Length; index++)
            Check(answer.Overview[index].Text, answer.Overview[index].References, $"overview[{index}]");
        for (var index = 0; index < answer.Summaries.Length; index++)
        {
            var summary = answer.Summaries[index];
            var location = $"summaries[{index}]";
            Check(summary.Text, summary.References, location);
            if (summary.References is not null && summary.References
                .Where(refs.ContainsKey).Any(reference => refs[reference].DocumentId != summary.DocumentId))
                Add("summary_source_invalid", location, "The summary document identity differs from its cited evidence.");
        }
        for (var index = 0; index < answer.Quotations.Length; index++)
        {
            var quote = answer.Quotations[index];
            if (!refs.TryGetValue(quote.Reference, out var evidence) || string.IsNullOrWhiteSpace(quote.Text)
                || !Normalize(evidence.Text).Contains(Normalize(quote.Text), StringComparison.Ordinal))
                Add("quotation_invalid", $"quotations[{index}]", "The quotation does not match its cited passage.");
        }
        for (var index = 0; index < answer.Images.Length; index++)
        {
            var image = answer.Images[index];
            var location = $"images[{index}]";
            if (image.Image < 1 || image.Image > imageCount)
                Add("image_number_invalid", location, "The image number does not match an attached image.");
            if (string.IsNullOrWhiteSpace(image.Uncertainty))
                Add("image_uncertainty_missing", location, "The image description does not state uncertainty.");
            if (!IsSingleSentence(image.Description))
                Add("image_description_invalid", location, "The image description is not one complete sentence.");
        }
        foreach (var duplicate in answer.Images.GroupBy(image => image.Image).Where(group => group.Count() > 1))
            Add("image_duplicate", "images", $"Image {duplicate.Key} has more than one description.");
        if (imageCount > 0)
            foreach (var missing in Enumerable.Range(1, imageCount).Where(number => answer.Images.All(image => image.Image != number)))
                Add("image_missing", "images", $"Image {missing} has no description.");
        return issues.ToArray();
    }

    public static bool IsSingleSentence(string? description) =>
        !string.IsNullOrWhiteSpace(description) && !description.Contains('\n') && !description.Contains('\r')
        && SentenceEnding().Count(description.Trim()) == 1
        && description.TrimEnd()[^1] is '.' or '!' or '?';

    public static string Normalize(string text) => Whitespace().Replace(text, " ").Trim();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
    [GeneratedRegex(@"\A[0-9]+:([0-9]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderCitationLabel();
    [GeneratedRegex(@"[.!?](?=\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceEnding();
}
