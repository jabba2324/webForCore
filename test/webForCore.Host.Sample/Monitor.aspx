<%@ Page Language="C#" %>
<%@ Import Namespace="System.IO" %>
<%@ Import Namespace="System.Linq" %>
<%@ Import Namespace="System.Web.Caching" %>
<script runat="server">
    // File change monitoring of many folders (#145, #146, #152), for test/webForCore.Host.Tests (FileChangeNotificationTests).
    // ?watch=200: a cache dependency on x.txt (which doesn't exist yet) in each of 200 folders of App_Data, kept in the
    // cache so that the folders stay monitored, as Orchard, Kooboo and nopCommerce keep theirs (the issue's repro, with
    // the dependencies kept). ?change=150: creates x.txt in folder 150 and waits up to 10 s for its cache entry to go.
    // Each answer has the number of inotify instances this process holds (Linux).
    static int Instances() {
        var fds = new DirectoryInfo("/proc/self/fd");
        if (!fds.Exists) return -1;
        return fds.GetFileSystemInfos().Count(f => {
            try { return f.LinkTarget != null && f.LinkTarget.Contains("inotify"); }
            catch (IOException) { return false; }
        });
    }

    protected void Page_Load(object sender, EventArgs e) {
        Response.ContentType = "text/plain";
        string watch = Request.QueryString["watch"], change = Request.QueryString["change"];
        if (watch != null) {
            int monitored = 0;
            for (int i = 0; i < int.Parse(watch); i++) {
                var dir = Server.MapPath("~/App_Data/folders/f" + i);
                Directory.CreateDirectory(dir);
                try {
                    Cache.Insert("f" + i, i, new CacheDependency(Path.Combine(dir, "x.txt")));
                    monitored++;
                }
                catch (Exception ex) {
                    Response.Write("failed at folder " + i + ": " + ex.GetBaseException().Message + ";");
                    break;
                }
            }
            Response.Write("monitored=" + monitored + ";");
        }
        if (change != null) {
            string key = "f" + change;
            bool cached = Cache[key] != null;
            File.WriteAllText(Server.MapPath("~/App_Data/folders/" + key + "/x.txt"), "changed");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Cache[key] != null && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(100);
            Response.Write("cached=" + cached + ";changed=" + (Cache[key] == null) + ";");
        }
        if (Request.QueryString["move"] != null) {
            // A file moved from one monitored folder to another: gone from the first, new in the second.
            string from = Server.MapPath("~/App_Data/folders/f31/from.txt"), to = Server.MapPath("~/App_Data/folders/f30/to.txt");
            File.WriteAllText(from, "moved");
            Cache.Insert("from", 1, new CacheDependency(from));
            Cache.Insert("to", 1, new CacheDependency(to));
            File.Move(from, to);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((Cache["from"] != null || Cache["to"] != null) && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(100);
            Response.Write("removed=" + (Cache["from"] == null) + ";added=" + (Cache["to"] == null) + ";");
        }
        Response.Write("instances=" + Instances() + ";process=" + System.Environment.ProcessId + ";");
    }
</script>
