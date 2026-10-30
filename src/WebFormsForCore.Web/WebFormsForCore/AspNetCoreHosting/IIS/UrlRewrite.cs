#if NETCOREAPP
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using static WebFormsForCore.Hosting.IIS.WebServerConfiguration;

namespace WebFormsForCore.Hosting.IIS
{
	// webForCore: the URL Rewrite module's inbound rules (<rewrite><rules>), distributed as IIS distributes them: the rules
	// of each web.config level (and <location>) apply to the URLs below it, matched against the URL relative to that level,
	// the parent's rules first; a rule's relative rewrite or redirect URL is relative to its level, one starting with '/'
	// to the site. ASP.NET Core's rewrite middleware parses the rules (Microsoft.AspNetCore.Rewrite reads the IIS format)
	// and applies them one by one here, so stopProcessing and redirects end the whole chain as in IIS.
	sealed class InboundRewrite
	{
		// A rule's '/'-rooted URL is marked so that it escapes its level's path base when applied.
		const string SiteRoot = "/__webForCore.siteRoot__";

		readonly WebServerConfiguration config;
		readonly ILogger logger;
		readonly ConcurrentDictionary<string, IList<Microsoft.AspNetCore.Rewrite.IRule>> rules = new ConcurrentDictionary<string, IList<Microsoft.AspNetCore.Rewrite.IRule>>();
		readonly ConcurrentDictionary<string, IFileProvider> providers = new ConcurrentDictionary<string, IFileProvider>(StringComparer.OrdinalIgnoreCase);
		int version = -1;

		public InboundRewrite(WebServerConfiguration config, ILogger logger)
		{
			this.config = config;
			this.logger = logger;
		}

		// Runs the rules that apply to the request's URL. True when a rule ended the response (redirect, custom
		// response, abort).
		public bool Apply(HttpContext context, XElement section)
		{
			var all = Items(section, "rewrite/rules").ToList();
			if (all.Count == 0) return false;
			if (version != config.Version)
			{
				rules.Clear();
				version = config.Version;
			}
			var maps = Element(section, "rewrite/rewriteMaps");
			int i = 0;
			while (i < all.Count)
			{
				var level = Attr(all[i], LevelPathAttribute.ToString(), "/");
				var group = new List<XElement>();
				while (i < all.Count && Attr(all[i], LevelPathAttribute.ToString(), "/") == level) group.Add(all[i++]);
				switch (ApplyGroup(context, Level(level), group, maps))
				{
					case RuleResult.EndResponse: return true;
					case RuleResult.SkipRemainingRules: return false;
				}
			}
			return false;
		}

		// A <location> may name a directory without its trailing slash.
		string Level(string level)
		{
			if (level.EndsWith("/")) return level;
			var mapped = Path.Combine(config.PhysicalRoot, level.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
			return Directory.Exists(mapped) ? level + "/" : level.Substring(0, level.LastIndexOf('/') + 1);
		}

		RuleResult ApplyGroup(HttpContext context, string level, List<XElement> group, XElement maps)
		{
			var request = context.Request;
			var prefix = level.TrimEnd('/');
			var path = request.Path.Value ?? "/";
			if (prefix.Length > 0 && !(path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)))
				return RuleResult.ContinueRules; // a parent's rule rewrote the URL out of this level
			var compiled = Rules(level, group, maps);
			if (compiled.Count == 0) return RuleResult.ContinueRules;

			var originalBase = request.PathBase;
			if (prefix.Length > 0)
			{
				request.PathBase = originalBase.Add(path.Substring(0, prefix.Length));
				request.Path = path.Length > prefix.Length ? path.Substring(prefix.Length) : "/";
			}
			var rewriteContext = new RewriteContext
			{
				HttpContext = context,
				StaticFileProvider = Provider(level),
				Logger = logger,
				Result = RuleResult.ContinueRules,
			};
			try
			{
				foreach (var rule in compiled)
				{
					rule.ApplyRule(rewriteContext);
					if (rewriteContext.Result != RuleResult.ContinueRules) break;
				}
			}
			finally
			{
				var rewritten = request.Path.Value ?? "/";
				request.PathBase = originalBase;
				if (rewritten.StartsWith(SiteRoot, StringComparison.Ordinal)) rewritten = rewritten.Length > SiteRoot.Length ? rewritten.Substring(SiteRoot.Length) : "/";
				else if (prefix.Length > 0) rewritten = prefix + rewritten;
				request.Path = rewritten;
				var location = context.Response.Headers.Location.ToString();
				if (location.Contains(SiteRoot, StringComparison.Ordinal))
					context.Response.Headers.Location = location.Replace(prefix + SiteRoot, "").Replace(SiteRoot, "");
			}
			return rewriteContext.Result;
		}

		IList<Microsoft.AspNetCore.Rewrite.IRule> Rules(string level, List<XElement> group, XElement maps)
		{
			var rulesElement = new XElement("rules", group.Select(rule =>
			{
				var copy = new XElement(rule);
				copy.Attribute(LevelPathAttribute)?.Remove();
				var action = copy.Element("action");
				var url = Attr(action, "url");
				if (level != "/" && url != null && url.StartsWith("/")) action.SetAttributeValue("url", SiteRoot + url);
				return copy;
			}));
			var rewrite = new XElement("rewrite", rulesElement);
			if (maps != null) rewrite.Add(new XElement(maps));
			var xml = new XElement("configuration", new XElement("system.webServer", rewrite)).ToString();
			return rules.GetOrAdd(level + "\u0001" + xml, _ =>
			{
				try
				{
					return new RewriteOptions().AddIISUrlRewrite(new StringReader(xml)).Rules.ToList();
				}
				catch (Exception e) when (e is FormatException || e is NotSupportedException || e is InvalidOperationException || e is ArgumentException)
				{
					logger.LogError(e, "The <rewrite> rules for {Level} cannot be used.", level);
					return Array.Empty<Microsoft.AspNetCore.Rewrite.IRule>();
				}
			});
		}

		// isFile/isDirectory conditions resolve {REQUEST_FILENAME} under the level's directory.
		IFileProvider Provider(string level) => providers.GetOrAdd(level, l =>
		{
			var directory = Path.Combine(config.PhysicalRoot, l.Trim('/').Replace('/', Path.DirectorySeparatorChar));
			return new PhysicalFileProvider(Directory.Exists(directory) ? directory : config.PhysicalRoot);
		});
	}

	// webForCore: the URL Rewrite module's outbound rules (<rewrite><outboundRules>), which ASP.NET Core does not have:
	// a rule with match serverVariable="RESPONSE_Name" rewrites that response header; otherwise it rewrites the response
	// body, either the attributes of the tags in filterByTags (A, Area, Base, Form, Frame, Head, IFrame, Img, Input, Link,
	// Script and <customTags>) or, without filterByTags, every match in the body. Preconditions (usually on
	// {RESPONSE_CONTENT_TYPE}), conditions, negate, the ECMAScript/Wildcard/ExactMatch syntaxes, {R:n} and {C:n}
	// back-references, server variables, rewrite maps and {ToLower:}/{UrlEncode:}/{UrlDecode:} are supported. A body is
	// rewritten only if it is not already compressed (IIS answers 500.52 instead).
	sealed class OutboundRewrite
	{
		static readonly Dictionary<string, string[]> TagAttributes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
		{
			["A"] = new[] { "href" },
			["Area"] = new[] { "href" },
			["Base"] = new[] { "href" },
			["Form"] = new[] { "action" },
			["Frame"] = new[] { "src", "longdesc" },
			["Head"] = new[] { "profile" },
			["IFrame"] = new[] { "src", "longdesc" },
			["Img"] = new[] { "src", "longdesc", "usemap" },
			["Input"] = new[] { "src", "usemap" },
			["Link"] = new[] { "href" },
			["Script"] = new[] { "src" },
		};

		readonly ILogger logger;
		readonly ConcurrentDictionary<string, Rule[]> compiled = new ConcurrentDictionary<string, Rule[]>();

		public OutboundRewrite(ILogger logger)
		{
			this.logger = logger;
		}

		sealed class Pattern
		{
			public Regex Regex;
			public string Exact;
			public bool Negate;

			public Match Match(string input, out bool matched)
			{
				Match m = null;
				if (Exact != null) matched = string.Equals(input, Exact, StringComparison.OrdinalIgnoreCase);
				else
				{
					m = Regex.Match(input ?? "");
					matched = m.Success;
				}
				if (Negate) { matched = !matched; m = null; }
				return m;
			}
		}

		sealed class Condition
		{
			public string Input;
			public Pattern Pattern;
		}

		sealed class Rule
		{
			public string Name;
			public bool StopProcessing;
			public string ServerVariable;           // RESPONSE_x: a header rule
			public List<(Regex Tag, string[] Attributes)> Tags; // null: the whole body
			public Pattern Match;
			public List<Condition> PreConditions;
			public bool PreConditionsMatchAny;
			public List<Condition> Conditions;
			public bool ConditionsMatchAny;
			public bool Rewrite;                    // action type="Rewrite" (else None)
			public string Value;
			public Dictionary<string, (Dictionary<string, string> Map, string Default)> Maps;
		}

		Rule[] Compile(XElement section)
		{
			var outbound = Element(section, "rewrite/outboundRules");
			if (outbound == null) return Array.Empty<Rule>();
			var maps = Element(section, "rewrite/rewriteMaps");
			var key = outbound.ToString() + (maps?.ToString() ?? "");
			return compiled.GetOrAdd(key, _ =>
			{
				try
				{
					return Build(section, outbound, maps);
				}
				catch (Exception e) when (e is ArgumentException || e is FormatException)
				{
					logger.LogError(e, "The <outboundRules> in web.config cannot be used.");
					return Array.Empty<Rule>();
				}
			});
		}

		static Rule[] Build(XElement section, XElement outbound, XElement mapsElement)
		{
			var maps = (mapsElement?.Elements("rewriteMap") ?? Enumerable.Empty<XElement>()).ToDictionary(
				m => Attr(m, "name", ""),
				m => (m.Elements("add").GroupBy(a => Attr(a, "key", ""), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => Attr(g.Last(), "value", ""), StringComparer.OrdinalIgnoreCase), Attr(m, "defaultValue", "")),
				StringComparer.OrdinalIgnoreCase);
			var preConditions = Items(section, "rewrite/outboundRules/preConditions").ToDictionary(p => Attr(p, "name", ""), StringComparer.OrdinalIgnoreCase);
			var customTags = Items(section, "rewrite/outboundRules/customTags").ToDictionary(t => Attr(t, "name", ""), StringComparer.OrdinalIgnoreCase);
			var result = new List<Rule>();
			foreach (var element in outbound.Elements("rule").Where(r => Bool(r, "enabled", true)))
			{
				var syntax = Attr(element, "patternSyntax", "ECMAScript");
				var match = element.Element("match");
				var action = element.Element("action");
				var rule = new Rule
				{
					Name = Attr(element, "name"),
					StopProcessing = Bool(element, "stopProcessing", false),
					ServerVariable = Attr(match, "serverVariable"),
					Match = Compile(Attr(match, "pattern", ".*"), syntax, Bool(match, "ignoreCase", true), Bool(match, "negate", false)),
					Rewrite = Attr(action, "type", "Rewrite") == "Rewrite",
					Value = Attr(action, "value", ""),
					Maps = maps,
				};
				var conditions = element.Element("conditions");
				rule.Conditions = Conditions(conditions, syntax);
				rule.ConditionsMatchAny = Attr(conditions, "logicalGrouping", "MatchAll") == "MatchAny";
				var preConditionName = Attr(element, "preCondition");
				if (!string.IsNullOrEmpty(preConditionName) && preConditions.TryGetValue(preConditionName, out var pre))
				{
					rule.PreConditions = Conditions(pre, Attr(pre, "patternSyntax", "ECMAScript"));
					rule.PreConditionsMatchAny = Attr(pre, "logicalGrouping", "MatchAll") == "MatchAny";
				}
				var filter = Attr(match, "filterByTags");
				if (rule.ServerVariable == null && !string.IsNullOrWhiteSpace(filter))
				{
					var attributes = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
					void Add(string tag, string attribute)
					{
						if (!attributes.TryGetValue(tag, out var set)) attributes[tag] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						set.Add(attribute);
					}
					foreach (var name in filter.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0))
					{
						if (name.Equals("CustomTags", StringComparison.OrdinalIgnoreCase))
						{
							if (customTags.TryGetValue(Attr(match, "customTags", ""), out var tags))
								foreach (var tag in tags.Elements("tag")) Add(Attr(tag, "name", ""), Attr(tag, "attribute", ""));
						}
						else if (TagAttributes.TryGetValue(name, out var known))
							foreach (var attribute in known) Add(name, attribute);
					}
					rule.Tags = attributes.Select(a => (
						new Regex(@"<" + Regex.Escape(a.Key) + @"\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
						a.Value.ToArray())).ToList();
				}
				result.Add(rule);
			}
			return result.ToArray();
		}

		static List<Condition> Conditions(XElement parent, string syntax) =>
			(parent?.Elements("add") ?? Enumerable.Empty<XElement>()).Select(c => new Condition
			{
				Input = Attr(c, "input", ""),
				Pattern = Compile(Attr(c, "pattern", ".*"), syntax, Bool(c, "ignoreCase", true), Bool(c, "negate", false)),
			}).ToList();

		static Pattern Compile(string pattern, string syntax, bool ignoreCase, bool negate)
		{
			var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : 0);
			return syntax switch
			{
				"ExactMatch" => new Pattern { Exact = pattern, Negate = negate },
				"Wildcard" => new Pattern { Regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", "(.*)").Replace("\\?", "(.)") + "$", options), Negate = negate },
				_ => new Pattern { Regex = new Regex(pattern, options), Negate = negate },
			};
		}

		// Runs the response through the outbound rules of the request's section.
		public async Task Invoke(HttpContext context, XElement section, Func<Task> next)
		{
			var rules = Compile(section);
			if (rules.Length == 0)
			{
				await next();
				return;
			}
			var headerRules = rules.Where(r => r.ServerVariable != null && r.ServerVariable.StartsWith("RESPONSE_", StringComparison.OrdinalIgnoreCase)).ToList();
			if (headerRules.Count > 0)
				context.Response.OnStarting(() => { RewriteHeaders(context, rules); return Task.CompletedTask; });
			var contentRules = rules.Where(r => r.ServerVariable == null).ToList();
			if (contentRules.Count == 0)
			{
				await next();
				return;
			}
			var bodyFeature = context.Features.Get<IHttpResponseBodyFeature>();
			var buffer = new BufferingStream(bodyFeature.Stream, () => ShouldBuffer(context, contentRules));
			context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(buffer, bodyFeature));
			try
			{
				await next();
			}
			finally
			{
				context.Features.Set(bodyFeature);
			}
			var data = buffer.Release();
			if (data == null)
			{
				await buffer.FlushAsync();
				return;
			}
			var encoding = Encoding(context.Response.ContentType);
			var text = encoding.GetString(data);
			var rewritten = RewriteContent(context, contentRules, text);
			var bytes = ReferenceEquals(rewritten, text) ? data : encoding.GetBytes(rewritten);
			if (!context.Response.HasStarted) context.Response.ContentLength = bytes.Length;
			if (!HttpMethods.IsHead(context.Request.Method)) await bodyFeature.Stream.WriteAsync(bytes);
		}

		bool ShouldBuffer(HttpContext context, List<Rule> contentRules)
		{
			var response = context.Response;
			if (response.StatusCode == 204 || response.StatusCode == 304 || HttpMethods.IsHead(context.Request.Method)) return false;
			if (!string.IsNullOrEmpty(response.Headers.ContentEncoding))
			{
				logger.LogWarning("Outbound rewrite rules skipped for {Path}: the response is already encoded ({Encoding}).", context.Request.Path, response.Headers.ContentEncoding.ToString());
				return false;
			}
			return contentRules.Any(r => PreConditionsMet(context, r));
		}

		bool PreConditionsMet(HttpContext context, Rule rule)
		{
			if (rule.PreConditions == null || rule.PreConditions.Count == 0) return true;
			return Evaluate(context, rule, rule.PreConditions, rule.PreConditionsMatchAny, null, out _);
		}

		// The conditions in order; with MatchAll the captures of the last one are {C:n}.
		bool Evaluate(HttpContext context, Rule rule, List<Condition> conditions, bool matchAny, Match ruleMatch, out Match last)
		{
			last = null;
			if (conditions.Count == 0) return true;
			foreach (var condition in conditions)
			{
				var input = Expand(context, rule, condition.Input, ruleMatch, last);
				var m = condition.Pattern.Match(input, out bool matched);
				if (matched)
				{
					last = m ?? last;
					if (matchAny) return true;
				}
				else if (!matchAny) return false;
			}
			return !matchAny;
		}

		void RewriteHeaders(HttpContext context, Rule[] rules)
		{
			foreach (var rule in rules)
			{
				if (rule.ServerVariable == null || !rule.ServerVariable.StartsWith("RESPONSE_", StringComparison.OrdinalIgnoreCase)) continue;
				if (!PreConditionsMet(context, rule)) continue;
				var header = rule.ServerVariable.Substring("RESPONSE_".Length).Replace('_', '-');
				var value = context.Response.Headers[header].ToString();
				var m = rule.Match.Match(value, out bool matched);
				if (!matched || !Evaluate(context, rule, rule.Conditions, rule.ConditionsMatchAny, m, out var conditionMatch)) continue;
				if (rule.Rewrite) context.Response.Headers[header] = Expand(context, rule, rule.Value, m, conditionMatch);
				if (rule.StopProcessing) break;
			}
		}

		string RewriteContent(HttpContext context, List<Rule> rules, string text)
		{
			foreach (var rule in rules)
			{
				if (!PreConditionsMet(context, rule)) continue;
				bool any = false;
				string Replace(string value)
				{
					var m = rule.Match.Match(value, out bool matched);
					if (!matched || !Evaluate(context, rule, rule.Conditions, rule.ConditionsMatchAny, m, out var conditionMatch)) return value;
					any = true;
					return rule.Rewrite ? Expand(context, rule, rule.Value, m, conditionMatch) : value;
				}
				if (rule.Tags == null)
				{
					if (rule.Match.Regex == null || rule.Match.Negate) text = Replace(text);
					else text = rule.Match.Regex.Replace(text, m =>
					{
						if (!Evaluate(context, rule, rule.Conditions, rule.ConditionsMatchAny, m, out var conditionMatch)) return m.Value;
						any = true;
						return rule.Rewrite ? Expand(context, rule, rule.Value, m, conditionMatch) : m.Value;
					});
				}
				else
				{
					foreach (var (tag, attributes) in rule.Tags)
					{
						var attribute = new Regex(@"(?<pre>\s(?:" + string.Join("|", attributes.Select(Regex.Escape)) + @")\s*=\s*)(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s""'>]+))",
							RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
						text = tag.Replace(text, t => attribute.Replace(t.Value, a =>
						{
							var value = a.Groups["v"];
							var replaced = Replace(value.Value);
							if (replaced == value.Value) return a.Value;
							return a.Value.Substring(0, value.Index - a.Index) + replaced + a.Value.Substring(value.Index - a.Index + value.Length);
						}));
					}
				}
				if (any && rule.StopProcessing) break;
			}
			return text;
		}

		static readonly Regex Reference = new Regex(@"\{([^{}]*)\}", RegexOptions.CultureInvariant);

		// {R:n}, {C:n}, server variables, functions and rewrite maps; inner references first.
		static string Expand(HttpContext context, Rule rule, string template, Match ruleMatch, Match conditionMatch)
		{
			if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template ?? "";
			for (int pass = 0; pass < 8; pass++)
			{
				bool changed = false;
				template = Reference.Replace(template, m =>
				{
					changed = true;
					return Resolve(context, rule, m.Groups[1].Value, ruleMatch, conditionMatch);
				});
				if (!changed || template.IndexOf('{') < 0) break;
			}
			return template;
		}

		static string Resolve(HttpContext context, Rule rule, string reference, Match ruleMatch, Match conditionMatch)
		{
			int colon = reference.IndexOf(':');
			if (colon > 0)
			{
				var name = reference.Substring(0, colon);
				var argument = reference.Substring(colon + 1);
				switch (name.ToUpperInvariant())
				{
					case "R": return Group(ruleMatch, argument);
					case "C": return Group(conditionMatch, argument);
					case "TOLOWER": return argument.ToLowerInvariant();
					case "TOUPPER": return argument.ToUpperInvariant();
					case "URLENCODE": return WebUtility.UrlEncode(argument);
					case "URLDECODE": return WebUtility.UrlDecode(argument);
					case "ESCAPEDATASTRING": return Uri.EscapeDataString(argument);
				}
				if (rule.Maps != null && rule.Maps.TryGetValue(name, out var map))
					return map.Map.TryGetValue(argument, out var mapped) ? mapped : map.Default;
				return "";
			}
			return ServerVariable(context, reference);
		}

		static string Group(Match match, string index) =>
			match != null && int.TryParse(index, out int i) && i < match.Groups.Count ? match.Groups[i].Value : "";

		static string ServerVariable(HttpContext context, string name)
		{
			var request = context.Request;
			var upper = name.ToUpperInvariant();
			if (upper.StartsWith("RESPONSE_")) return context.Response.Headers[name.Substring(9).Replace('_', '-')].ToString();
			if (upper.StartsWith("HTTP_") && upper != "HTTP_HOST" && upper != "HTTPS") return request.Headers[name.Substring(5).Replace('_', '-')].ToString();
			return upper switch
			{
				"HTTP_HOST" => request.Host.Value,
				"HTTPS" => request.IsHttps ? "on" : "off",
				"URL" => request.PathBase + request.Path,
				"PATH_INFO" => request.PathBase + request.Path,
				"QUERY_STRING" => request.QueryString.HasValue ? request.QueryString.Value.Substring(1) : "",
				"REQUEST_URI" => context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? (request.PathBase + request.Path + request.QueryString).ToString(),
				"REQUEST_METHOD" => request.Method,
				"SERVER_NAME" => request.Host.Host,
				"SERVER_PORT" => (context.Connection.LocalPort).ToString(),
				"SERVER_PROTOCOL" => request.Protocol,
				"REMOTE_ADDR" => context.Connection.RemoteIpAddress?.ToString() ?? "",
				"REMOTE_HOST" => context.Connection.RemoteIpAddress?.ToString() ?? "",
				"REMOTE_PORT" => context.Connection.RemotePort.ToString(),
				"CONTENT_TYPE" => request.ContentType ?? "",
				_ => "",
			};
		}

		static Encoding Encoding(string contentType)
		{
			var charset = contentType == null ? null : Regex.Match(contentType, @"charset\s*=\s*""?([^;""\s]+)", RegexOptions.IgnoreCase).Groups[1].Value;
			if (!string.IsNullOrEmpty(charset))
			{
				try { return System.Text.Encoding.GetEncoding(charset); }
				catch (ArgumentException) { }
			}
			return System.Text.Encoding.UTF8;
		}

		// Passes the body through, or holds all of it when the rules apply (decided at the first write or flush).
		sealed class BufferingStream : Stream
		{
			readonly Stream inner;
			readonly Func<bool> shouldBuffer;
			MemoryStream buffer;
			bool decided;

			public BufferingStream(Stream inner, Func<bool> shouldBuffer) { this.inner = inner; this.shouldBuffer = shouldBuffer; }

			void Decide()
			{
				if (decided) return;
				decided = true;
				if (shouldBuffer()) buffer = new MemoryStream();
			}

			// The held body (deciding now if nothing was written), or null when it was passed through.
			public byte[] Release()
			{
				Decide();
				var data = buffer?.ToArray();
				buffer = null;
				return data;
			}

			public override void Write(byte[] data, int offset, int count) { Decide(); if (buffer != null) buffer.Write(data, offset, count); else inner.Write(data, offset, count); }
			public override async Task WriteAsync(byte[] data, int offset, int count, CancellationToken cancellationToken)
			{
				Decide();
				if (buffer != null) buffer.Write(data, offset, count); else await inner.WriteAsync(data, offset, count, cancellationToken);
			}
			public override async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
			{
				Decide();
				if (buffer != null) buffer.Write(data.Span); else await inner.WriteAsync(data, cancellationToken);
			}
			public override void Flush() { if (decided && buffer == null) inner.Flush(); }
			public override Task FlushAsync(CancellationToken cancellationToken) => decided && buffer == null ? inner.FlushAsync(cancellationToken) : Task.CompletedTask;
			public override bool CanRead => false;
			public override bool CanSeek => false;
			public override bool CanWrite => true;
			public override long Length => throw new NotSupportedException();
			public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
			public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
			public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
			public override void SetLength(long value) => throw new NotSupportedException();
		}
	}
}
#endif
