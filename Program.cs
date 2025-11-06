// Program.cs
// Target: .NET 6+ (Console App)
// העתקי לקובץ Program.cs בפרויקט Console ותריצי.
// הסבר קצר: יש כאן את כל המחלקות הדרושות: HtmlElement, HtmlHelper (Singleton),
// Selector, Parser/Builder ו-HtmlQuery. בסוף נמצאת דוגמה להרצה.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HtmlProcessorDemo
{
    // ----- HtmlElement -----
    public class HtmlElement
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty; // tag name
        public List<KeyValuePair<string,string>> Attributes { get; } = new();
        public List<string> Classes { get; } = new();
        public string InnerHtml { get; set; } = string.Empty;

        public HtmlElement? Parent { get; set; }
        public List<HtmlElement> Children { get; } = new();

        public override int GetHashCode()
        {
            // מבוסס על reference (ברירת מחדל), אבל נהפוך אותו ליציב:
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj);
        }

        // Descendants - באמצעות Queue (לא רק ריקורסיה)
        public IEnumerable<HtmlElement> Descendants()
        {
            var q = new Queue<HtmlElement>();
            q.Enqueue(this);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                yield return cur;
                foreach (var c in cur.Children)
                    q.Enqueue(c);
            }
        }

        // Ancestors - פשוט עלייה בשרשרת ההורים
        public IEnumerable<HtmlElement> Ancestors()
        {
            var cur = this.Parent;
            while (cur != null)
            {
                yield return cur;
                cur = cur.Parent;
            }
        }

        // Checks if matches a selector node (single TagName/Id/Classes set)
        public bool MatchesSelectorNode(Selector node)
        {
            if (!string.IsNullOrEmpty(node.TagName))
            {
                if (!string.Equals(node.TagName, this.Name, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.IsNullOrEmpty(node.Id))
            {
                if (!string.Equals(node.Id, this.Id, StringComparison.Ordinal))
                    return false;
            }

            foreach (var cls in node.Classes)
            {
                if (!this.Classes.Contains(cls))
                    return false;
            }

            return true;
        }

        public override string ToString()
        {
            var idPart = string.IsNullOrEmpty(Id) ? "" : $" id=\"{Id}\"";
            var classPart = Classes.Count==0 ? "" : $" class=\"{string.Join(" ", Classes)}\"";
            return $"<{Name}{idPart}{classPart}>";
        }
    }

    // ----- HtmlHelper (Singleton) -----
    public sealed class HtmlHelper
    {
        private static readonly Lazy<HtmlHelper> _lazy = new(() => new HtmlHelper());
        public static HtmlHelper Instance => _lazy.Value;

        public string[] AllTags { get; private set; } = Array.Empty<string>();
        public string[] VoidTags { get; private set; } = Array.Empty<string>();

        private HtmlHelper()
        {
            // מנסה לטעון מקבצי JSON בשם tags.json ו-void-tags.json
            // אבל אם לא קיימים — נשתמש ב-fallback מובנה.
            try
            {
                if (File.Exists("tags.json"))
                {
                    var j = File.ReadAllText("tags.json");
                    AllTags = JsonSerializer.Deserialize<string[]>(j) ?? Array.Empty<string>();
                }
                if (File.Exists("void-tags.json"))
                {
                    var j = File.ReadAllText("void-tags.json");
                    VoidTags = JsonSerializer.Deserialize<string[]>(j) ?? Array.Empty<string>();
                }
            }
            catch
            {
                // נגונן נגד שגיאות IO — נשתמש ב-fallback
            }

            if (AllTags.Length == 0)
            {
                // fallback פשוט למטרת הדגמה (אפשר להרחיב לפי הצורך)
                AllTags = new string[] {
                    "html","head","body","div","span","p","a","img","ul","ol","li","br","meta","input","form","label","section","article","nav","h1","h2","h3","h4","h5","h6","script","style"
                };
            }
            if (VoidTags.Length == 0)
            {
                VoidTags = new string[] { "br","img","meta","input","link","hr" };
            }
        }

        public bool IsHtmlTag(string name)
        {
            return Array.Exists(AllTags, t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsVoidTag(string name)
        {
            return Array.Exists(VoidTags, t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ----- Selector -----
    public class Selector
    {
        public string TagName { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public List<string> Classes { get; } = new();

        public Selector? Parent { get; set; }
        public Selector? Child { get; set; }

        // ממירה מחרוזת סלקטור כמו: "div#my.id1.class2 span.other" לשרשרת של Selector
        public static Selector Parse(string selectorString)
        {
            // מחלקים לפי רווחים לרמות
            var parts = Regex.Split(selectorString.Trim(), @"\s+");
            var root = new Selector();
            var cur = root;
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;

                // הפרדת name/#/.
                // נעבור על המחרוזת ונבנה
                var tag = string.Empty;
                var id = string.Empty;
                var classes = new List<string>();

                // Use regex to capture sequences: tag? then #id parts and .class parts
                // דוגמא: div#myid.class1.class2
                var regex = new Regex(@"(^[a-zA-Z][\w-:]*)|(#([\w-:]+))|(\.([\w-:]+))");
                var matches = regex.Matches(p);
                foreach (Match m in matches)
                {
                    if (m.Groups[1].Success)
                    {
                        tag = m.Groups[1].Value;
                    }
                    else if (m.Groups[3].Success)
                    {
                        id = m.Groups[3].Value;
                    }
                    else if (m.Groups[5].Success)
                    {
                        classes.Add(m.Groups[5].Value);
                    }
                }

                // אם לא זיהינו שם תג — נבדוק אם זה שם תקין
                if (!string.IsNullOrEmpty(tag))
                {
                    // רק אם זה תג html תקני
                    if (HtmlHelper.Instance.IsHtmlTag(tag))
                        cur.TagName = tag;
                    else
                        cur.TagName = tag; // אפשר להשאיר גם אם לא ברשימה
                }

                if (!string.IsNullOrEmpty(id)) cur.Id = id;
                foreach (var c in classes) cur.Classes.Add(c);

                // אם יש עוד חלקים — ניצור בן
                if (p != parts[^1]) // זהו חלק לא אחרון? (טכנית לא מדויק אם יש יותר מאלמנטים זה בסדר)
                {
                    var next = new Selector();
                    cur.Child = next;
                    next.Parent = cur;
                    cur = next;
                }
                else
                {
                    // אם לא אחרון - אבל יש יותר אף פעם? פשוט נמשיך לולאה
                }

                // אם יש עוד חלקים בשורה — נמשיך בלולאה. אולם הקוד לעיל לא מזהה אינדקס נכון.
            }

            // הבעיה הקטנה: אם יש יותר ממקטעים, הלוגיקה עם p != parts[^1] לא ממש יעילה.
            // נבנה מחדש בצורה ברורה:
            // לכן נממש גרסה נקייה יותר מטה:
            return ParseRobust(selectorString);
        }

        private static Selector ParseRobust(string selectorString)
        {
            var parts = Regex.Split(selectorString.Trim(), @"\s+");
            Selector? root = null;
            Selector? cur = null;
            foreach (var p in parts)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var s = new Selector();

                var regex = new Regex(@"(^[a-zA-Z][\w-:]*)|(#([\w-:]+))|(\.([\w-:]+))");
                var matches = regex.Matches(p);
                foreach (Match m in matches)
                {
                    if (m.Groups[1].Success)
                        s.TagName = m.Groups[1].Value;
                    else if (m.Groups[3].Success)
                        s.Id = m.Groups[3].Value;
                    else if (m.Groups[5].Success)
                        s.Classes.Add(m.Groups[5].Value);
                }

                if (root == null)
                {
                    root = s;
                    cur = s;
                }
                else
                {
                    cur!.Child = s;
                    s.Parent = cur;
                    cur = s;
                }
            }

            return root ?? new Selector();
        }

        // לקבלת כל הרמות כסולקציונרים ברשימה (root -> child -> ...)
        public IEnumerable<Selector> Flatten()
        {
            var cur = this;
            while (cur != null)
            {
                yield return cur;
                cur = cur.Child!;
            }
        }

        public override string ToString()
        {
            var cls = Classes.Count==0 ? "" : $".{string.Join(".", Classes)}";
            var id = string.IsNullOrEmpty(Id) ? "" : $"#{Id}";
            var tag = string.IsNullOrEmpty(TagName) ? "" : TagName;
            return $"{tag}{id}{cls}";
        }
    }

    // ----- Parser & Tree Builder -----
    public static class HtmlParser
    {
        // קורא את HTML מ-URL
        public static async Task<string> LoadFromUrlAsync(string url)
        {
            using var client = new HttpClient();
            var resp = await client.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync();
            return html;
        }

        // מחלק את ה־HTML ל"טוקנים" (תגיות וטקסט)
        // regex: תופס תגיות <...> או טקסט בין תגיות
        private static readonly Regex TokenRegex = new(@"<[^>]+>|[^<]+", RegexOptions.Compiled);

        // attribute regex
        private static readonly Regex AttrRegex = new(@"(?<name>[\w\-:]+)(?:\s*=\s*(?:""(?<v>[^""]*)""|'(?<v2>[^']*)'|(?<v3>[^\s""'>]+)))?", RegexOptions.Compiled);

        // שם תג מתוך תג פתיחה כמו "<div class='x'>"
        private static string ExtractTagName(string tagToken)
        {
            // הורדת התחילית "<" וסופית ">" או "/>"
            var t = tagToken.Trim();
            t = t.TrimStart('<').TrimEnd('>');
            t = t.Trim();
            // הסרת "/" בסוף (self closing)
            if (t.EndsWith("/")) t = t[..^1].Trim();
            // שמירת המילה הראשונה
            var m = Regex.Match(t, @"^\/?\s*([^\s/>]+)");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }

        // בונה עץ HtmlElement מה־html text
        public static HtmlElement BuildTree(string html)
        {
            var root = new HtmlElement { Name = "document" };
            var current = root;

            var matches = TokenRegex.Matches(html);
            foreach (Match m in matches)
            {
                var token = m.Value;
                if (token.StartsWith("<"))
                {
                    // תג — בדיקות
                    if (token.StartsWith("<!--")) // תגית הערה
                    {
                        // דילוג על תגי הערה
                        continue;
                    }
                    else if (token.StartsWith("</"))
                    {
                        // תג סגירה -> עלייה לאב
                        var tagName = ExtractTagName(token).ToLowerInvariant();
                        // נעלה עד שנמצא אב שמתאים (כדי לשרוד מבנים לא מאוזנים)
                        var temp = current;
                        while (temp != null && !string.Equals(temp.Name, tagName, StringComparison.OrdinalIgnoreCase))
                        {
                            temp = temp.Parent;
                        }
                        if (temp != null && temp.Parent != null)
                        {
                            current = temp.Parent;
                        }
                        // אחרת התעלמות
                    }
                    else if (token.StartsWith("<!")) // doctype וכו'
                    {
                        continue;
                    }
                    else
                    {
                        // תג פתיחה או self-closing
                        var tagName = ExtractTagName(token);
                        var element = new HtmlElement { Name = tagName };

                        // parse attributes מתוך הטקסט שבין שם התג לסוף
                        // ניקוי התחלתי: הסרת "<tag" ו־">"
                        var inner = token.Trim().TrimStart('<').TrimEnd('>').Trim();
                        // הסרת שם התג מה־inner:
                        var firstSpace = inner.IndexOfAny(new char[] { ' ', '\t', '\r', '\n' });
                        var attrsPart = firstSpace >= 0 ? inner[(firstSpace + 1)..].Trim() : string.Empty;
                        if (firstSpace < 0) attrsPart = string.Empty;

                        foreach (Match am in AttrRegex.Matches(attrsPart))
                        {
                            var name = am.Groups["name"].Value;
                            var val = string.Empty;
                            if (am.Groups["v"].Success) val = am.Groups["v"].Value;
                            else if (am.Groups["v2"].Success) val = am.Groups["v2"].Value;
                            else if (am.Groups["v3"].Success) val = am.Groups["v3"].Value;

                            element.Attributes.Add(new KeyValuePair<string,string>(name, val));
                            if (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase))
                                element.Id = val;
                            if (string.Equals(name, "class", StringComparison.OrdinalIgnoreCase))
                            {
                                var classes = Regex.Split(val.Trim(), @"\s+");
                                foreach (var c in classes)
                                    if (!string.IsNullOrEmpty(c))
                                        element.Classes.Add(c);
                            }
                        }

                        // בדיקת self-closing: אם מסתיים ב "/>" או אם tag ברשימת void tags
                        var isSelfClosing = token.EndsWith("/>") || HtmlHelper.Instance.IsVoidTag(tagName);

                        // הוספה לשורש הילדים של current
                        element.Parent = current;
                        current.Children.Add(element);

                        if (!isSelfClosing)
                        {
                            // נכנסים פנימה
                            current = element;
                        }
                        // אם self-closing - לא משנים את current
                    }
                }
                else
                {
                    // טקסט פנימי - הוסף ל-InnerHtml של current
                    var text = token;
                    // ניקוי רווחים מיותרים בתחילת ובסוף
                    var cleaned = text.Replace("\r", "").Replace("\n", "").Trim();
                    if (!string.IsNullOrEmpty(cleaned))
                    {
                        // אם כבר יש InnerHtml, נחבר עם רווח
                        if (string.IsNullOrEmpty(current.InnerHtml))
                            current.InnerHtml = cleaned;
                        else
                            current.InnerHtml += " " + cleaned;
                    }
                }
            }

            return root;
        }
    }

    // ----- HtmlQuery -----
    public static class HtmlQuery
    {
        // פונקציה שמקבלת root ו-selector string ומחזירה רשימת אלמנטים
        public static List<HtmlElement> Query(HtmlElement root, string selectorString)
        {
            var selectorRoot = Selector.Parse(selectorString);
            // הרשימה של כל הרמות
            var selectors = new List<Selector>();
            var it = selectorRoot;
            while (it != null)
            {
                selectors.Add(it);
                it = it.Child!;
            }

            // אם אין selectors — אין תוצאות
            if (selectors.Count == 0) return new List<HtmlElement>();

            // עבור הרמה הראשונה — נחפש כל אלה שעונים בתור descendants מ-root
            var currentMatches = new List<HtmlElement>();

            foreach (var d in root.Descendants())
            {
                if (d.MatchesSelectorNode(selectors[0]))
                    currentMatches.Add(d);
            }

            // עבור כל רמה בהמשך — ניקח עבור כל match את צאצאיו שיגיבו לרמה הבאה
            for (int level = 1; level < selectors.Count; level++)
            {
                var nextMatches = new List<HtmlElement>();
                var sel = selectors[level];
                foreach (var match in currentMatches)
                {
                    foreach (var d in match.Descendants())
                    {
                        if (d.MatchesSelectorNode(sel))
                            nextMatches.Add(d);
                    }
                }

                currentMatches = nextMatches;
            }

            // מניעת כפילויות בעזרת HashSet (בהתבסס על reference equality)
            var set = new HashSet<HtmlElement>(currentMatches);
            return new List<HtmlElement>(set);
        }
    }

    // ----- Program / Demo -----

class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("HTML Processor Demo");

            // אופציה: לקרוא מדוגמא מובנית או מ-URL
            Console.WriteLine("Select an option: 1 = Built-in example, 2 = Load from URL");
            var c = Console.ReadLine();
            string html;
            if (c == "2")
            {
                Console.Write("Enter URL (e.g. https://example.com): ");
                var url = Console.ReadLine() ?? "https://example.com";
                try
                {
                    html = await HtmlParser.LoadFromUrlAsync(url);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error loading from URL, switching to local example. Error: " + ex.Message);
                    html = SampleHtml;
                }
            }
            else
            {
                html = SampleHtml;
            }

            var root = HtmlParser.BuildTree(html);
            Console.WriteLine("HTML tree successfully built.");

            // הדגמה: חיפוש סלקטורים
            var examples = new string[]
            {
            "div",
            "#main",
            ".item",
            "div#container .item",
            "ul li.item",
            "div p"
            };

            foreach (var ex in examples)
            {
                var result = HtmlQuery.Query(root, ex);
                Console.WriteLine($"\nSelector: \"{ex}\" -> {result.Count} results");
                foreach (var r in result)
                {
                    Console.WriteLine($"  {r}  InnerHtml='{(r.InnerHtml.Length > 50 ? r.InnerHtml[..50] + "..." : r.InnerHtml)}'");
                }
            }

            Console.WriteLine("\nDemo completed. Press ENTER to exit.");
            Console.ReadLine();
        }


        // דוגמת HTML פשוטה לבדיקה מקומית
        private static string SampleHtml = @"
<!doctype html>
<html>
  <head>
    <meta charset='utf-8' />
    <title>Demo</title>
  </head>
  <body>
    <div id='main'>
      <div id='container' class='wrapper'>
        <p class='intro'>Hello world</p>
        <div class='item' id='i1'>Item 1</div>
        <div class='item'>Item 2 <span class='badge'>New</span></div>
        <ul class='list'>
          <li class='item'>Li 1</li>
          <li>Li 2</li>
          <li class='item'>Li 3</li>
        </ul>
      </div>
      <p>Another paragraph</p>
    </div>
  </body>
</html>
";
    }
}
