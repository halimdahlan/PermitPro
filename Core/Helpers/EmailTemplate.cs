using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PermitPro.Core.Helpers;

/// <summary>
/// Minimal template engine for the emails (replaced Scriban). Syntax:
/// <list type="bullet">
/// <item><c>{{name}}</c> — value of <c>name</c>, HTML-encoded (unknown names print nothing).</item>
/// <item><c>{{#if name}}…{{/if}}</c> — kept only when <c>name</c> is a non-empty text or a non-empty list.</item>
/// <item><c>{{#each list}}…{{/each}}</c> — repeated per item; inside, <c>{{field}}</c> reads the item first, then the outer values.</item>
/// </list>
/// Names are snake_case, case-insensitive. A malformed template throws <see cref="FormatException"/>.
/// </summary>
public static class EmailTemplate
{
	private static readonly Regex Tag = new(@"\{\{\s*(?:(?<kind>#if|#each|/if|/each)\s*)?(?<name>[A-Za-z_]\w*)?\s*\}\}", RegexOptions.Compiled);

	private abstract record Node;
	private sealed record TextNode(string Text) : Node;
	private sealed record VarNode(string Name) : Node;
	private sealed record BlockNode(bool IsEach, string Name, List<Node> Body) : Node;

	/// <param name="encode">HTML-encode values (true for the body, false for plain text such as the subject).</param>
	public static string Render(string template, IReadOnlyDictionary<string, object?> model, bool encode = true)
	{
		var nodes = Parse(template);
		var sb = new StringBuilder();
		RenderNodes(nodes, new List<IReadOnlyDictionary<string, object?>> { model }, sb, encode);
		return sb.ToString();
	}

	/// <summary>Throws <see cref="FormatException"/> when the template has unbalanced tags.</summary>
	public static void Validate(string template) => Parse(template);

	private static List<Node> Parse(string template)
	{
		var root = new List<Node>();
		var stack = new Stack<(BlockNode Block, List<Node> Parent)>();
		var current = root;
		var pos = 0;

		foreach (Match m in Tag.Matches(template))
		{
			if (m.Index > pos)
				current.Add(new TextNode(template[pos..m.Index]));
			pos = m.Index + m.Length;

			var kind = m.Groups["kind"].Value;
			var name = m.Groups["name"].Value;

			switch (kind)
			{
				case "#if":
				case "#each":
					if (name.Length == 0) throw new FormatException($"'{m.Value}' needs a name.");
					var block = new BlockNode(kind == "#each", name, new List<Node>());
					current.Add(block);
					stack.Push((block, current));
					current = block.Body;
					break;

				case "/if":
				case "/each":
					if (stack.Count == 0) throw new FormatException($"'{m.Value}' has no matching opening tag.");
					var open = stack.Pop();
					if (open.Block.IsEach != (kind == "/each"))
						throw new FormatException($"'{m.Value}' does not match '{(open.Block.IsEach ? "#each" : "#if")} {open.Block.Name}'.");
					current = open.Parent;
					break;

				default:
					if (name.Length == 0) throw new FormatException("Empty placeholder '{{}}'.");
					current.Add(new VarNode(name));
					break;
			}
		}

		if (stack.Count > 0)
			throw new FormatException($"'{(stack.Peek().Block.IsEach ? "#each" : "#if")} {stack.Peek().Block.Name}' is never closed.");

		if (pos < template.Length)
			current.Add(new TextNode(template[pos..]));

		return root;
	}

	private static void RenderNodes(List<Node> nodes, List<IReadOnlyDictionary<string, object?>> scopes, StringBuilder sb, bool encode)
	{
		foreach (var node in nodes)
		{
			switch (node)
			{
				case TextNode t:
					sb.Append(t.Text);
					break;

				case VarNode v:
					var text = Lookup(scopes, v.Name) switch
					{
						null => "",
						string s => s,
						var o => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? "",
					};
					sb.Append(encode ? WebUtility.HtmlEncode(text) : text);
					break;

				case BlockNode { IsEach: false } b:
					if (IsTruthy(Lookup(scopes, b.Name)))
						RenderNodes(b.Body, scopes, sb, encode);
					break;

				case BlockNode b:
					if (Lookup(scopes, b.Name) is IEnumerable<IReadOnlyDictionary<string, object?>> items)
					{
						foreach (var item in items)
						{
							scopes.Add(item);
							RenderNodes(b.Body, scopes, sb, encode);
							scopes.RemoveAt(scopes.Count - 1);
						}
					}
					break;
			}
		}
	}

	// Innermost scope first.
	private static object? Lookup(List<IReadOnlyDictionary<string, object?>> scopes, string name)
	{
		for (var i = scopes.Count - 1; i >= 0; i--)
		{
			foreach (var kv in scopes[i])
			{
				if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
					return kv.Value;
			}
		}
		return null;
	}

	private static bool IsTruthy(object? value) => value switch
	{
		null => false,
		string s => !string.IsNullOrWhiteSpace(s),
		bool b => b,
		IEnumerable<IReadOnlyDictionary<string, object?>> items => items.Any(),
		_ => true,
	};
}
