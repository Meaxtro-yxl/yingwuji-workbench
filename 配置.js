// ────────────────────────────────────────────────────────────────
// 工作台配置 · 只改这一份
//
// 改完怎么生效：
//   桌面应用：关掉重开（配置只在启动时读一次）
//   浏览器：刷新页面
//
// 每个字段的意思见 README 的「配置怎么改」一节。
// 这里写的是默认值，照着改成你自己的就行。
// ────────────────────────────────────────────────────────────────
window.WORKBENCH_CONFIG = {

  // 窗口标题、侧栏品牌。shortName 是左上角那两个字
  "name": "内容爆款工作台",
  "shortName": "工作台",
  "subtitle": "RADAR",

  // 首页问候语，例如「阿明」。留空就是「早上好。」
  "greeting": "",

  // 主页文件名；appId 用来做单实例锁和本地缓存目录名
  "homepage": "工作台.html",
  "appId": "ContentWorkbench",

  // 应用里「打开文件」按钮允许打开的文件和文件夹（防止被任意路径利用）
  "openable": [
    "配置.js", "收件箱.md", "规则.md", "爆款库.md", "采集指令.md",
    "README.md", "草稿", "灵感.md", "审核结果.md", "热点词.txt", "素材", "数据.js"
  ],

  // 你做的平台。id 要和 数据.js 里每条作品的 pf 对上；
  // 只留你真正在做的平台，界面上只会显示这些。
  "platforms": [
    { "id": "dy",   "name": "抖音",      "color": "#1a1a1a" },
    { "id": "xhs",  "name": "小红书",    "color": "#e2412f" },
    { "id": "x",    "name": "X",         "color": "#555555" },
    { "id": "yt",   "name": "YouTube",   "color": "#ff0033" },
    { "id": "ig",   "name": "Instagram", "color": "#c13584" },
    { "id": "jj",   "name": "掘金",      "color": "#1e80ff" },
    { "id": "scys", "name": "生财有术",  "color": "#d4a017" }
  ],

  // 主平台：用它在界面上标「这个平台还没人做」
  "primaryPlatform": "dy",

  // 分级阈值。倍数 =（赞 + 评 + 藏）÷ 粉丝数
  // 改这里，同时把 规则.md 里的表改成一样的，不然两边对不上
  "grading": {
    "aFans": 500,       // A 级：粉丝上限
    "aMult": 30,        // A 级：倍数下限
    "bFans": 2000,      // B 级：粉丝上限
    "bMult": 10,        // B 级：倍数下限
    "cMult": 5,         // C 级：倍数下限（C 到 B 之间都算 C）
    "dropFans": 10000,  // 粉丝超过这个数直接不收
    "minFans": 10       // 粉丝低于这个数不收，倍数没参考意义
  },

  // 你自己的账号：粉丝数会显示在「规则」页
  "account": { "name": "", "fans": 0 },

  // 你的赛道关键词，会列在「搜索词」页；采集指令里也用同一批词
  "keywords": [],

  // 热点页顶部那行提示，留空用默认文案
  "trendHint": "",

  // 热点源。两种写法：
  //   newsnow 源：{"id":"juejin","name":"掘金","domain":"juejin.cn"}
  //   RSS 源：    {"id":"某博客","name":"某博客","url":"https://…/feed","domain":"…","rss":true}
  // 下面这些默认是科技类，赛道不搭就直接删掉，换成你行业的 RSS。
  "feeds": [
    { "id": "juejin",                 "name": "掘金",   "domain": "juejin.cn" },
    { "id": "ithome",                 "name": "IT之家", "domain": "ithome.com" },
    { "id": "v2ex-share",             "name": "V2EX",   "domain": "v2ex.com" },
    { "id": "github-trending-today",  "name": "GitHub", "domain": "github.com" },
    { "id": "hackernews",             "name": "HN",     "domain": "news.ycombinator.com" },
    { "id": "qbitai",                 "name": "量子位", "url": "https://www.qbitai.com/feed", "domain": "qbitai.com", "rss": true },
    { "id": "simonw",                 "name": "Simon Willison", "url": "https://simonwillison.net/atom/everything/", "domain": "simonwillison.net", "rss": true }
  ]
};
