// 内容爆款工作台 · 桌面壳
// WinForms + WebView2，加载工作台文件夹里的 工作台.html。
// 只做四件事：开窗口、替页面写文件、数据.js 变了就自动刷新、抓热点（页面跨域抓不了）。
// 名称、主页、平台、热点源都从 配置.js 读，壳里不放任何个人设定。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);

    [STAThread]
    static void Main()
    {
        bool first;
        var root = FindRoot();
        var cfg = AppConfig.Load(root);
        using (var m = new Mutex(true, cfg.appId + ".Single", out first))
        {
            if (!first) return; // 已经开着一个了
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } // 高分屏不糊
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(root, cfg));
        }
    }

    // 从 exe 所在目录往上找 配置.js（或默认主页），exe 放哪都行
    static string FindRoot()
    {
        var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (int i = 0; i < 5 && d != null; i++, d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "配置.js")) || File.Exists(Path.Combine(d.FullName, "工作台.html")))
                return d.FullName;
        return null;
    }
}

class MainForm : Form
{
    const string Host = "workbench.local";

    readonly WebView2 web = new WebView2();
    readonly string root;
    readonly AppConfig cfg;
    readonly string[] openable;
    CoreWebView2Environment env;
    FileSystemWatcher watcher;
    System.Windows.Forms.Timer debounce;

    public MainForm(string root, AppConfig cfg)
    {
        this.root = root;
        this.cfg = cfg;
        // 页面只能让壳打开配置里列出的这几个，防止被任意路径利用
        openable = cfg.openable != null && cfg.openable.Length > 0
            ? cfg.openable
            : new[] { "收件箱.md", "规则.md", "爆款库.md", "采集指令.md", "README.md", "草稿", "灵感.md", "审核结果.md", "热点词.txt" };
        Text = cfg.name;
        Width = 1480; Height = 960;
        MinimumSize = new System.Drawing.Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        web.Dock = DockStyle.Fill;
        Controls.Add(web);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (root == null)
        {
            MessageBox.Show("没找到 配置.js / 工作台.html。\n把本程序放在工作台文件夹里（或它的子文件夹）再打开。", Text);
            Close(); return;
        }
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), cfg.appId);
            env = await CoreWebView2Environment.CreateAsync(null, data);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show("WebView2 启动失败：" + ex.Message + "\n\n需要 Microsoft Edge WebView2 Runtime。", Text);
            Close(); return;
        }
        var cw = web.CoreWebView2;
        cw.Settings.IsStatusBarEnabled = false;
        cw.Settings.AreDefaultContextMenusEnabled = true;
        cw.SetVirtualHostNameToFolderMapping(Host, root, CoreWebView2HostResourceAccessKind.Allow);
        // 本地文件随时会被改写，一律不缓存
        cw.AddWebResourceRequestedFilter("https://" + Host + "/*", CoreWebView2WebResourceContext.All);
        cw.WebResourceRequested += (s, a) => a.Request.Headers.SetHeader("Cache-Control", "no-cache");
        cw.WebMessageReceived += OnMessage;
        // 外链（作品来源）交给系统浏览器；热点页的链接走下面的阅读器
        cw.NewWindowRequested += (s, a) => { a.Handled = true; OpenExternal(a.Uri); };
        cw.NavigationStarting += (s, a) =>
        {
            if (!new Uri(a.Uri).Host.Equals(Host, StringComparison.OrdinalIgnoreCase)) { a.Cancel = true; OpenExternal(a.Uri); }
        };
        cw.Navigate("https://" + Host + "/" + cfg.homepage);
        WatchData();
    }

    static void OpenExternal(string uri)
    {
        Uri u;
        if (Uri.TryCreate(uri, UriKind.Absolute, out u) && (u.Scheme == "https" || u.Scheme == "http"))
            Process.Start(u.AbsoluteUri);
    }
    // 页面 → 壳：{cmd:"saveReview", md:"..."} / {cmd:"open", name:"规则.md"}
    void OnMessage(object s, CoreWebView2WebMessageReceivedEventArgs a)
    {
        string reply;
        try
        {
            var msg = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(a.WebMessageAsJson);
            var cmd = msg.ContainsKey("cmd") ? msg["cmd"] as string : null;
            if (cmd == "fetchTrends") { FetchTrends(); return; } // 异步，抓完自己回话
            if (cmd == "browse") { ShowReader(msg["url"] as string, msg); return; }
            if (cmd == "browseRect") { PlaceReader(msg); return; }
            if (cmd == "browseClose") { HideReader(); return; }
            if (cmd == "browseBack") { if (reader != null && reader.CoreWebView2 != null && reader.CoreWebView2.CanGoBack) reader.CoreWebView2.GoBack(); return; }
            if (cmd == "browseOut") { if (reader != null && reader.CoreWebView2 != null) OpenExternal(reader.CoreWebView2.Source); HideReader(); return; }
            if (cmd == "saveReview")
            {
                var md = msg["md"] as string ?? "";
                // 固定文件名，每次覆盖：页面本来就持有全量审核状态
                File.WriteAllText(Path.Combine(root, "审核结果.md"), md, new UTF8Encoding(false));
                reply = Ok("saved");
            }
            else if (cmd == "saveIdeas")
            {
                // json 是唯一数据源，先写临时文件再替换，写一半崩了也不会丢旧数据
                var json = msg["json"] as string ?? "[]";
                new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json); // 不是合法 JSON 就拒绝，别把文件写坏
                WriteAtomic(Path.Combine(root, "灵感.json"), json);
                WriteAtomic(Path.Combine(root, "灵感.md"), msg["md"] as string ?? "");
                reply = Ok("ideas");
            }
            else if (cmd == "setTrend")
            {
                var id = msg["id"] as string;
                var st = msg.ContainsKey("status") ? msg["status"] as string : null;
                if (st != null && st != "idea" && st != "ignored") throw new InvalidOperationException("状态不对");
                var f = LoadTrends();
                var it = f.items.FirstOrDefault(x => x.id == id);
                if (it == null) throw new InvalidOperationException("这条热点已经过期了");
                it.status = st;
                SaveTrends(f);
                reply = Ok("trend");
            }
            else if (cmd == "open")
            {
                var name = msg["name"] as string;
                if (Array.IndexOf(openable, name) < 0) throw new InvalidOperationException("不允许打开 " + name);
                var p = Path.Combine(root, name);
                if (!File.Exists(p) && !Directory.Exists(p)) throw new FileNotFoundException(name + " 不存在");
                Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
                reply = Ok("opened");
            }
            else throw new InvalidOperationException("未知指令");
        }
        catch (Exception ex) { reply = Err(ex.Message); }
        web.CoreWebView2.PostWebMessageAsJson(reply);
    }

    // ── 阅读器：热点页点开的外链在壳里看，不跳系统浏览器 ──
    // 页面画顶栏、报告正文区域的位置（物理像素），壳把第二个 WebView2 盖在那块上面。
    // 阅读器是独立的 CoreWebView2：没有 radar.local 映射，也收不到页面那条消息通道，外站碰不到本地文件
    WebView2 reader;

    async void ShowReader(string url, Dictionary<string, object> msg)
    {
        Uri u;
        if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https") return;
        if (reader == null)
        {
            reader = new WebView2 { Visible = false };
            Controls.Add(reader);
            await reader.EnsureCoreWebView2Async(env);
            var rc = reader.CoreWebView2;
            rc.Settings.IsStatusBarEnabled = false;
            // 站内再弹新窗口，就地打开
            rc.NewWindowRequested += (s, a) => { a.Handled = true; rc.Navigate(a.Uri); };
            rc.NavigationStarting += (s, a) =>
            {
                Uri v;
                if (a.Uri == "about:blank") return; // 收起时用它停掉声音
                if (!Uri.TryCreate(a.Uri, UriKind.Absolute, out v) || (v.Scheme != "https" && v.Scheme != "http")) a.Cancel = true;
            };
            EventHandler<object> report = (s, a) => ReportReader();
            rc.DocumentTitleChanged += report;
            rc.SourceChanged += (s, a) => ReportReader();
            rc.HistoryChanged += report;
            // 焦点在阅读器里时页面收不到按键，Esc 由壳转告
            reader.KeyDown += (s, a) => { if (a.KeyCode == Keys.Escape) { HideReader(); Post(new { ok = true, what = "browseClosed" }); } };
        }
        PlaceReader(msg);
        reader.Visible = true;
        reader.BringToFront();
        reader.CoreWebView2.Navigate(u.AbsoluteUri);
        reader.Focus();
    }

    void PlaceReader(Dictionary<string, object> msg)
    {
        if (reader == null) return;
        reader.SetBounds(Convert.ToInt32(msg["x"]), Convert.ToInt32(msg["y"]), Math.Max(1, Convert.ToInt32(msg["w"])), Math.Max(1, Convert.ToInt32(msg["h"])));
    }

    void HideReader()
    {
        if (reader == null || !reader.Visible) return;
        reader.Visible = false;
        if (reader.CoreWebView2 != null) reader.CoreWebView2.Navigate("about:blank"); // 停掉视频声音
        web.Focus();
    }

    void ReportReader()
    {
        if (reader == null || reader.CoreWebView2 == null || !reader.Visible) return;
        var rc = reader.CoreWebView2;
        if (rc.Source == "about:blank") return;
        Post(new { ok = true, what = "browseState", title = rc.DocumentTitle, url = rc.Source, back = rc.CanGoBack });
    }

    void Post(object o)
    {
        if (web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(o));
    }

    // ── 热点：上游信号。源写在 配置.js 的 feeds 里（newsnow 聚合接口或 RSS），关键词写在 热点词.txt ──
    static readonly Feed[] DefaultSources =
    {
        new Feed { id = "juejin", name = "掘金", domain = "juejin.cn" },
        new Feed { id = "ithome", name = "IT之家", domain = "ithome.com" },
        new Feed { id = "v2ex-share", name = "V2EX", domain = "v2ex.com" },
        new Feed { id = "github-trending-today", name = "GitHub", domain = "github.com" },
        new Feed { id = "hackernews", name = "HN", domain = "news.ycombinator.com" },
        new Feed { id = "qbitai", name = "量子位", url = "https://www.qbitai.com/feed", domain = "qbitai.com", rss = true },
        new Feed { id = "simonw", name = "Simon Willison", url = "https://simonwillison.net/atom/everything/", domain = "simonwillison.net", rss = true },
    };

    Feed[] Sources
    {
        get { return cfg.feeds != null && cfg.feeds.Length > 0 ? cfg.feeds : DefaultSources; }
    }
    const string NewsNow = "https://newsnow.busiyi.world/api/s?id={0}&latest";

    public class SrcState { public bool ok; public int count; public string error; }
    public class TrendItem { public string id, src, title, desc, url, hit, first, last, status; }
    public class TrendFile
    {
        public string updated;
        public Dictionary<string, SrcState> sources = new Dictionary<string, SrcState>();
        public List<TrendItem> items = new List<TrendItem>();
    }
    class Raw { public string title, desc, url; }

    bool fetching;

    string TrendsPath { get { return Path.Combine(root, "热点.json"); } }

    TrendFile LoadTrends()
    {
        try
        {
            if (File.Exists(TrendsPath))
            {
                var f = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<TrendFile>(File.ReadAllText(TrendsPath, Encoding.UTF8));
                if (f != null && f.items != null && f.sources != null) return f;
            }
        }
        catch { } // 文件坏了就当没有，下次抓取重建
        return new TrendFile();
    }

    void SaveTrends(TrendFile f)
    {
        WriteAtomic(TrendsPath, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(f));
    }

    async void FetchTrends()
    {
        if (fetching) return;
        fetching = true;
        string reply;
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var rules = KeywordRules.Load(Path.Combine(root, "热点词.txt"));
            using (var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromSeconds(10) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) " + cfg.appId + "/1.0");
                var tasks = Sources.Select(s => FetchOne(http, s)).ToArray();
                // 一个源挂了不影响其他源：FetchOne 自己吞异常
                await Task.WhenAll(tasks);

                var f = LoadTrends();
                var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                var byId = f.items.ToDictionary(x => x.id);
                for (int i = 0; i < Sources.Length; i++)
                {
                    var src = Sources[i];
                    var r = tasks[i].Result;
                    if (r.Item2 != null) { f.sources[src.id] = new SrcState { ok = false, error = r.Item2 }; continue; }
                    int n = 0;
                    foreach (var raw in r.Item1)
                    {
                        var hit = rules.Match(raw.title + " " + raw.desc);
                        if (hit == null) continue;
                        n++;
                        var id = Hash(raw.url);
                        TrendItem it;
                        if (byId.TryGetValue(id, out it)) { it.last = now; it.title = raw.title; it.desc = raw.desc; }
                        else { it = new TrendItem { id = id, src = src.id, title = raw.title, desc = raw.desc, url = raw.url, first = now, last = now }; f.items.Add(it); byId[id] = it; }
                    }
                    f.sources[src.id] = new SrcState { ok = true, count = n };
                }
                // 关键词改过也要生效：旧条目重新过一遍词；7 天没再上榜的清掉
                var cutoff = DateTime.Now.AddDays(-7).ToString("yyyy-MM-ddTHH:mm:ss");
                foreach (var it in f.items) it.hit = rules.Match(it.title + " " + it.desc);
                f.items = f.items.Where(x => string.CompareOrdinal(x.last, cutoff) >= 0 && (x.hit != null || x.status != null))
                                 .OrderByDescending(x => x.first).ToList();
                f.updated = now;
                SaveTrends(f);
            }
            reply = Ok("trends");
        }
        catch (Exception ex) { reply = Err("热点抓取失败：" + ex.Message); }
        finally { fetching = false; }
        if (web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(reply);
    }

    // 返回 (条目, 错误)。错误不为空就是这个源失败了
    static async Task<Tuple<List<Raw>, string>> FetchOne(HttpClient http, Feed s)
    {
        try
        {
            var list = new List<Raw>();
            if (!s.rss)
            {
                var text = await http.GetStringAsync(string.Format(NewsNow, Uri.EscapeDataString(s.id)));
                var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(text);
                var st = d.ContainsKey("status") ? d["status"] as string : null;
                if (st != "success" && st != "cache") return Tuple.Create(list, "接口返回 " + (st ?? "空"));
                var items = d.ContainsKey("items") ? d["items"] as System.Collections.ArrayList : null;
                if (items == null) return Tuple.Create(list, "没有条目");
                foreach (var o in items)
                {
                    var m = o as Dictionary<string, object>;
                    if (m == null) continue;
                    string desc = null;
                    var extra = m.ContainsKey("extra") ? m["extra"] as Dictionary<string, object> : null;
                    if (extra != null && extra.ContainsKey("hover")) desc = extra["hover"] as string;
                    Add(list, s, m.ContainsKey("title") ? m["title"] as string : null, desc, m.ContainsKey("url") ? m["url"] as string : null);
                }
            }
            else
            {
                var text = await http.GetStringAsync(s.url);
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                XDocument doc;
                using (var rd = XmlReader.Create(new StringReader(text), settings)) doc = XDocument.Load(rd);
                foreach (var e in doc.Descendants().Where(x => x.Name.LocalName == "item" || x.Name.LocalName == "entry").Take(30))
                {
                    var t = e.Elements().FirstOrDefault(x => x.Name.LocalName == "title");
                    var links = e.Elements().Where(x => x.Name.LocalName == "link").ToList();
                    string url = null;
                    foreach (var l in links)
                    {
                        var href = l.Attribute("href");
                        var rel = l.Attribute("rel");
                        if (href != null && (rel == null || rel.Value == "alternate")) { url = href.Value; break; }
                        if (href == null && !string.IsNullOrWhiteSpace(l.Value)) { url = l.Value.Trim(); break; }
                    }
                    Add(list, s, t == null ? null : t.Value, null, url);
                }
            }
            if (list.Count == 0) return Tuple.Create(list, "抓到 0 条");
            return Tuple.Create(list, (string)null);
        }
        catch (Exception ex)
        {
            var msg = ex is TaskCanceledException ? "超时" : ex.GetBaseException().Message;
            return Tuple.Create(new List<Raw>(), msg);
        }
    }

    static void Add(List<Raw> list, Feed s, string title, string desc, string url)
    {
        if (string.IsNullOrWhiteSpace(title) || !SafeUrl(url, s.domain)) return;
        list.Add(new Raw { title = Clean(title), desc = string.IsNullOrWhiteSpace(desc) ? null : Clean(desc), url = url });
    }

    // 照 TrendRadar 的 _check_domain_safety：只收 https 且域名对得上的链接
    static bool SafeUrl(string url, string domain)
    {
        Uri u;
        if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https") return false;
        var h = u.Host.ToLowerInvariant();
        return h == domain || h.EndsWith("." + domain);
    }

    static string Clean(string s)
    {
        s = WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", ""));
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > 300 ? s.Substring(0, 300) : s;
    }

    static string Hash(string s)
    {
        using (var sha = SHA1.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").Substring(0, 12).ToLowerInvariant();
    }

    static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }

    static string Ok(string what) { return new JavaScriptSerializer().Serialize(new { ok = true, what }); }
    static string Err(string m) { return new JavaScriptSerializer().Serialize(new { ok = false, error = m }); }

    // 「跑今日」重写 数据.js 后，窗口自己刷新，不用手动关开
    void WatchData()
    {
        debounce = new System.Windows.Forms.Timer { Interval = 600 };
        debounce.Tick += (s, e) => { debounce.Stop(); web.CoreWebView2.Reload(); };
        watcher = new FileSystemWatcher(root, "数据.js") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        FileSystemEventHandler h = (s, e) => BeginInvoke((Action)(() => { debounce.Stop(); debounce.Start(); }));
        watcher.Changed += h; watcher.Created += h;
        watcher.Renamed += (s, e) => h(s, null);
        watcher.EnableRaisingEvents = true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (watcher != null) watcher.Dispose();
        base.OnFormClosed(e);
    }
}

// 热点词.txt 的解析，TrendRadar frequency_words 语法的子集：
//   [GLOBAL_FILTER] 下每行一个全局排除词；[WORD_GROUPS] 下空行分组
//   普通词（组内任一命中）、/正则/、+必须词、!本组排除词；# 开头是注释
// 普通词不分大小写；正则默认也不分
class KeywordRules
{
    class Group { public List<Regex> any = new List<Regex>(); public List<string> anyName = new List<string>(); public List<Regex> must = new List<Regex>(); public List<Regex> not = new List<Regex>(); }
    readonly List<Regex> global = new List<Regex>();
    readonly List<Group> groups = new List<Group>();

    static Regex Make(string w)
    {
        var o = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var m = Regex.Match(w, @"^/(.+)/([a-z]*)$");
        try { return m.Success ? new Regex(m.Groups[1].Value, o, TimeSpan.FromMilliseconds(50)) : new Regex(Regex.Escape(w), o); }
        catch (ArgumentException) { return null; } // 正则写错了就跳过这一行
    }

    public static KeywordRules Load(string path)
    {
        var r = new KeywordRules();
        if (!File.Exists(path)) throw new FileNotFoundException("缺少 热点词.txt");
        string section = "WORD_GROUPS";
        Group g = null;
        foreach (var line0 in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = line0.Trim();
            if (line.StartsWith("#")) continue;
            if (line == "[GLOBAL_FILTER]" || line == "[WORD_GROUPS]") { section = line.Trim('[', ']'); g = null; continue; }
            if (line.Length == 0) { g = null; continue; }
            if (section == "GLOBAL_FILTER") { var x = Make(line); if (x != null) r.global.Add(x); continue; }
            if (g == null) { g = new Group(); r.groups.Add(g); }
            if (line[0] == '+' && line.Length > 1) { var x = Make(line.Substring(1)); if (x != null) g.must.Add(x); }
            else if (line[0] == '!' && line.Length > 1) { var x = Make(line.Substring(1)); if (x != null) g.not.Add(x); }
            else { var x = Make(line); if (x != null) { g.any.Add(x); g.anyName.Add(line); } }
        }
        r.groups.RemoveAll(x => x.any.Count == 0);
        return r;
    }

    // 命中返回命中的那个词（给界面显示），没命中返回 null
    public string Match(string text)
    {
        try
        {
            if (global.Any(x => x.IsMatch(text))) return null;
            foreach (var g in groups)
            {
                if (g.not.Any(x => x.IsMatch(text))) continue;
                if (!g.must.All(x => x.IsMatch(text))) continue;
                for (int i = 0; i < g.any.Count; i++)
                {
                    var m = g.any[i].Match(text);
                    if (m.Success) return m.Value;
                }
            }
        }
        catch (RegexMatchTimeoutException) { }
        return null;
    }
}

// 配置.js 里 window.WORKBENCH_CONFIG = {...} 那段，壳只用得上这几项
class Feed
{
    public string id { get; set; }
    public string name { get; set; }
    public string url { get; set; }
    public string domain { get; set; }
    public bool rss { get; set; }
}

class AppConfig
{
    public string name { get; set; }
    public string shortName { get; set; }
    public string subtitle { get; set; }
    public string greeting { get; set; }
    public string homepage { get; set; }
    public string appId { get; set; }
    public string[] openable { get; set; }
    public Feed[] feeds { get; set; }

    // 读不到、读坏了都用默认值，不让应用开不起来
    public static AppConfig Load(string root)
    {
        AppConfig c = null;
        try
        {
            var p = root == null ? null : Path.Combine(root, "配置.js");
            if (p != null && File.Exists(p))
            {
                var text = StripJsComments(File.ReadAllText(p, Encoding.UTF8));
                int a = text.IndexOf('{'), b = text.LastIndexOf('}');
                if (a >= 0 && b > a)
                {
                    var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    c = ser.Deserialize<AppConfig>(text.Substring(a, b - a + 1));
                }
            }
        }
        catch { }
        if (c == null) c = new AppConfig();
        if (string.IsNullOrWhiteSpace(c.name)) c.name = "内容爆款工作台";
        if (string.IsNullOrWhiteSpace(c.appId)) c.appId = "ContentWorkbench";
        if (string.IsNullOrWhiteSpace(c.homepage)) c.homepage = "工作台.html";
        return c;
    }

    // 配置.js 里允许写 // 注释，但 JSON 不认。这里只剥字符串外的注释，URL 里的 // 不受影响
    static string StripJsComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool inStr = false, esc = false;
        char q = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (inStr)
            {
                sb.Append(ch);
                if (esc) esc = false;
                else if (ch == '\\') esc = true;
                else if (ch == q) inStr = false;
                continue;
            }
            if (ch == '"' || ch == '\'')
            {
                inStr = true; q = ch; sb.Append(ch); continue;
            }
            if (ch == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                i += 2;
                while (i < s.Length && s[i] != '\n') i++;
                if (i < s.Length) sb.Append('\n');
                continue;
            }
            if (ch == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) i++;
                i++;
                continue;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
