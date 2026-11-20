using System.Text;

namespace CedarSharp.E1Prototype;

/// <summary>
/// Splits the concatenated Cedar policy text exported by <c>Dogwood.Lower</c>
/// into individual policy bodies without changing any bytes.
/// </summary>
/// <remarks>
/// <para>
/// The export is Cedar's <c>PolicySet</c> <c>Display</c> rendering: one policy
/// body after another, each terminated by a top-level <c>;</c>. This splitter
/// is string-aware and depth-aware, so semicolons inside string literals
/// (annotation values, entity ids, condition string constants) and inside
/// <c>when</c> blocks (which always sit inside braces) never split a policy.
/// </para>
/// <para>
/// It is only guaranteed for formatter-emitted text: Dogwood lowering renders
/// each policy from its AST, so the bodies contain no comments and no raw
/// newlines inside string literals. Every successful split reconstructs the
/// input exactly from its segments and separators; any deviation throws.
/// </para>
/// <para>
/// The order of the bodies follows Cedar's policy-set iteration order, which
/// the pinned bridge populates in Dogwood source order. That ordering is an
/// implementation property, not a documented contract, so every pairing of a
/// body to an exported policy ID is verified separately by the tests (see
/// <c>PolicyIdPreservationTests</c>).
/// </para>
/// </remarks>
public static class CedarPolicyTextSplitter
{
    /// <summary>Splits <paramref name="cedarPolicyText"/> into exact policy bodies.</summary>
    /// <exception cref="InvalidOperationException">The text is not a well-formed formatter rendering.</exception>
    public static IReadOnlyList<string> Split(string cedarPolicyText)
    {
        ArgumentNullException.ThrowIfNull(cedarPolicyText);
        var bodies = new List<string>();
        var separators = new List<string>();
        var start = 0;
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = 0; index < cedarPolicyText.Length; index++)
        {
            var current = cedarPolicyText[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (current)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                case '[':
                case '(':
                    depth++;
                    break;
                case '}':
                case ']':
                case ')':
                    depth--;
                    if (depth < 0)
                    {
                        throw new InvalidOperationException(
                            $"Unbalanced '{current}' at offset {index} in the exported Cedar policy text.");
                    }

                    break;
                case ';' when depth == 0:
                    var end = index + 1;
                    bodies.Add(cedarPolicyText[start..end]);
                    while (end < cedarPolicyText.Length && char.IsWhiteSpace(cedarPolicyText[end]))
                    {
                        end++;
                    }

                    separators.Add(cedarPolicyText[(index + 1)..end]);
                    start = end;
                    break;
            }
        }

        if (inString)
        {
            throw new InvalidOperationException("The exported Cedar policy text ends inside a string literal.");
        }

        if (depth != 0)
        {
            throw new InvalidOperationException("The exported Cedar policy text has unbalanced braces.");
        }

        if (start < cedarPolicyText.Length)
        {
            if (!string.IsNullOrWhiteSpace(cedarPolicyText[start..]))
            {
                throw new InvalidOperationException(
                    "The exported Cedar policy text has content after the final policy terminator.");
            }
        }

        // Trailing whitespace after the final ';' is part of the input; the
        // reconstruction below reproduces it, so it is not dropped.
        if (bodies.Count == 0 && cedarPolicyText.Length > 0)
        {
            throw new InvalidOperationException("The exported Cedar policy text contains no policy.");
        }

        // Lossless proof: segments plus the exact separators reproduce the input.
        var builder = new StringBuilder(cedarPolicyText.Length);
        for (var index = 0; index < bodies.Count; index++)
        {
            builder.Append(bodies[index]);
            if (index < separators.Count)
            {
                builder.Append(separators[index]);
            }
        }

        if (builder.ToString() != cedarPolicyText)
        {
            throw new InvalidOperationException("The split did not reconstruct the exported policy text exactly.");
        }

        return bodies;
    }
}
