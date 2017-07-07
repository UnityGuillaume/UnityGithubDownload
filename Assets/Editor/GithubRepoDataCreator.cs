using System.IO;
using System.Collections.Generic;
using System.CodeDom.Compiler;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;
using SimpleJSON;

public class GithubRepoDataCreator : EditorWindow
{
    public class Request
    {
        public delegate void RequestFinished(Request req);

        public string targetAddress;
        public UnityWebRequest request;
        public RequestFinished callback;
    }

    public class RepoData
    {
        static public int version = 4;

        public string etag;
        public string address;
        public string name;
        public string description;
        public string archiveURL;

        public string category;
        public string iconeBase64Encoded;

        public Request currentDownLoadRequest = null;
    }

    public class RepoListInfo
    {
        public string address;
        public string iconePath;
        public string category;
    }

    protected List<Request> _pendingRequests = new List<Request>();
    protected Dictionary<string, RepoListInfo> _reposList = new Dictionary<string, RepoListInfo>();
    protected Dictionary<string, RepoData> _reposData = new Dictionary<string, RepoData>();

    protected string _cacheFilePath = "";
    protected bool isLoading = false;

    public string _id = null;

    [MenuItem("Github Tools/Generate")]
    static void Open()
    {
        var win = GetWindow<GithubRepoDataCreator>();
        win.ShowPopup();
    }

    private void OnEnable()
    {
        isLoading = false;

        _cacheFilePath = Application.dataPath + "/../GithubDownloaderCache.json";

        _reposList = new Dictionary<string, RepoListInfo>();

        var repoListData = JSON.Parse(File.ReadAllText(Application.dataPath + "/Repositories.json"));
        var repoArray = repoListData["repos"].AsArray;

        for(int i =0; i < repoArray.Count; ++i)
        {
            RepoListInfo info = new RepoListInfo();
            info.address = ((JSONString)repoArray[i]["url"]);
            info.category = repoArray[i]["category"] == null ? "" : ((JSONString)repoArray[i]["category"]);
            info.iconePath = repoArray[i]["icone"] == null ? "" : ((JSONString)repoArray[i]["icone"]);

            _reposList.Add(info.address, info);
        }

        if(File.Exists(Application.dataPath + "/../login.txt"))
        {
            _id = File.ReadAllText(Application.dataPath + "/../login.txt");
            Debug.Log("found id will use that " + _id);
        }

        foreach(var keypair in _reposList)
        {
            Request req = new Request();
            req.targetAddress = keypair.Key;
            req.request = UnityWebRequest.Get("https://api.github.com/repos/" + req.targetAddress);

            if(_id != null)
                req.request.SetRequestHeader("AUTHORIZATION", authenticate(_id));
            //req.request.SetRequestHeader("If-Modified-Since", System.DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'UTC'"));
            req.callback = RetrievedRepoData;
            req.request.Send();

            _pendingRequests.Add(req);

            if (keypair.Value.iconePath != "")
            {
                Request iconeDL = new Request();
                iconeDL.targetAddress = req.targetAddress;
                iconeDL.request = UnityWebRequest.Get("https://api.github.com/repos/" + req.targetAddress + "/contents/" + keypair.Value.iconePath);
                iconeDL.request.SetRequestHeader("accept", "accept:application/vnd.github.v3.raw");

                if (_id != null)
                    iconeDL.request.SetRequestHeader("AUTHORIZATION", authenticate(_id));

                iconeDL.callback = RetrievedIconeData;
                iconeDL.request.Send();

                _pendingRequests.Add(iconeDL);
            }

            ////try to retrieve package info if exist
            //req = new Request();
            //req.targetAddress = _reposList[i];
            //req.request = UnityWebRequest.Get("https://api.github.com/repos/" + _reposList[i] + "/contents/package.json");
            //req.request.SetRequestHeader("accept", "accept:application/vnd.github.v3.raw");
            //req.callback = RetrievedPackageinfo;

            //if (_id != null)
            //    req.request.SetRequestHeader("AUTHORIZATION", authenticate(_id));

            //req.request.Send();

            //_pendingRequests.Add(req);
        }

        isLoading = true;
    }

    private void OnDisable()
    {
        WriteReposData();
    }

    private void Update()
    {
        for (int i = 0; i < _pendingRequests.Count; ++i)
        {
            if (_pendingRequests[i].request.isDone)
            {
                if (_pendingRequests[i].request.isNetworkError)
                {
                    Debug.LogError(_pendingRequests[i].request.error);
                }
                else
                {
                    _pendingRequests[i].callback(_pendingRequests[i]);
                }

                _pendingRequests.RemoveAt(i);
                i--;
            }
        }

        if (isLoading && _pendingRequests.Count == 0)
            Close();
    }

    void WriteReposData()
    {
        FileStream stream = new FileStream(_cacheFilePath, FileMode.Create);
        StreamWriter writer = new StreamWriter(stream);
        IndentedTextWriter idwriter = new IndentedTextWriter(writer);

        idwriter.WriteLine("{");
        idwriter.Indent += 1;
        idwriter.WriteLine("[");
        idwriter.Indent += 1;

        int i = 0;
        foreach (var data in _reposData.Values)
        {
            idwriter.WriteLine("{");
            idwriter.Indent += 1;

            idwriter.WriteLine("\"{0}\" : \"{1}\",", "name", data.name);
            idwriter.WriteLine("\"{0}\" : \"{1}\",", "desc", data.description);
            idwriter.WriteLine("\"{0}\" : \"{1}\",", "download", data.archiveURL);
            idwriter.WriteLine("\"{0}\" : \"{1}\",", "category", data.category == null? "" : data.category);
            idwriter.WriteLine("\"{0}\" : \"{1}\"", "icone", data.iconeBase64Encoded == null ? "" : data.iconeBase64Encoded);

            idwriter.Indent -= 1;

            if (i == _reposData.Count - 1)
                idwriter.WriteLine("}");
            else
                idwriter.WriteLine("},");

            i += 1;
        }

        idwriter.Indent -= 1;
        idwriter.WriteLine("]");
        idwriter.Indent -= 1;
        idwriter.WriteLine("}");

        idwriter.Close();
        writer.Close();
        stream.Close();
    }

    string authenticate(string usernamepassword)
    {
        string auth = usernamepassword;
        auth = System.Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(auth));
        auth = "Basic " + auth;
        return auth;
    }


    // ==== request callback 
    public void RetrievedRepoData(Request req)
    {
        if (req.request.responseCode == 404)
            return;

        var data = JSON.Parse(req.request.downloadHandler.text);

        if (data["private"].AsBool)
            return; //ignore any private repo

        RepoData repoData = null;
        if (!_reposData.TryGetValue(req.targetAddress, out repoData))
        {
            repoData = new RepoData();
            _reposData[req.targetAddress] = repoData;
        }

        repoData.address = req.targetAddress;
        repoData.name = data["name"] == null ? "" : ((JSONString)data["name"]);
        repoData.description = data["description"] == null ? "" : ((JSONString)data["description"]);

        repoData.archiveURL = data["archive_url"] == null ? "" : ((JSONString)data["archive_url"]);
        repoData.archiveURL = repoData.archiveURL.Replace("{archive_format}", "zipball");
        repoData.archiveURL = repoData.archiveURL.Replace("{/ref}", "");

        repoData.category = _reposList[req.targetAddress].category;

        repoData.etag = req.request.GetResponseHeader("ETag");
    }

    //public void RetrievedPackageinfo(Request req)
    //{
    //    if (req.request.responseCode == 404)
    //        return;

    //    var data = JSON.Parse(req.request.downloadHandler.text);

    //    RepoData repoData = null;
    //    if (!_reposData.TryGetValue(req.targetAddress, out repoData))
    //    {
    //        repoData = new RepoData();
    //        _reposData[req.targetAddress] = repoData;
    //    }

    //    repoData.category = data["category"] == null ? "" : ((JSONString)data["category"]);

    //    if (data["icone"] != null && ((JSONString)data["icone"]) != "")
    //    {
    //        Request iconeDL = new Request();
    //        iconeDL.targetAddress = req.targetAddress;
    //        iconeDL.request = UnityWebRequest.Get("https://api.github.com/repos/" + req.targetAddress + "/contents/" + data["icone"]);
    //        iconeDL.request.SetRequestHeader("accept", "accept:application/vnd.github.v3.raw");

    //        if (_id != null)
    //            iconeDL.request.SetRequestHeader("AUTHORIZATION", authenticate(_id));

    //        iconeDL.callback = RetrievedIconeData;
    //        iconeDL.request.Send();

    //        _pendingRequests.Add(iconeDL);
    //    }
    //}

    void RetrievedIconeData(Request req)
    {
        if (req.request.responseCode == 404)
            return;

        RepoData repoData = null;
        if (!_reposData.TryGetValue(req.targetAddress, out repoData))
        {
            repoData = new RepoData();
            _reposData[req.targetAddress] = repoData;
        }

        repoData.iconeBase64Encoded = System.Convert.ToBase64String(req.request.downloadHandler.data);
    }
}
