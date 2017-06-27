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

        public Request currentDownLoadRequest = null;
    }


    protected List<Request> _pendingRequests = new List<Request>();
    protected List<string> _reposList = new List<string>();
    protected Dictionary<string, RepoData> _reposData = new Dictionary<string, RepoData>();

    protected string _cacheFilePath = "";
    protected bool isLoading = false;

    [MenuItem("Github Tools/Generate")]
    static void Open()
    {
        var win = GetWindow<GithubRepoDataCreator>();
        win.ShowPopup();
    }

    private void OnEnable()
    {
        isLoading = false;

        _cacheFilePath = Application.dataPath + "/../Library/GithubDownloaderCache";

        _reposList = new List<string>();

        _reposList.Add("UnityGuillaume/Prototypes");
        _reposList.Add("UnityGuillaume/packagedesigner");
        _reposList.Add("UnityGuillaume/ImporterRule");

        for (int i = 0; i < _reposList.Count; ++i)
        {
            Request req = new Request();
            req.targetAddress = _reposList[i];
            req.request = UnityWebRequest.Get("https://api.github.com/repos/" + _reposList[i]);
            //req.request.SetRequestHeader("If-Modified-Since", System.DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'UTC'"));
            req.request.Send();
            req.callback = RetrievedRepoData;

            _pendingRequests.Add(req);
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
                if (_pendingRequests[i].request.isError)
                {
                    Debug.LogError(_pendingRequests[i].request.error);
                }
                else
                {
                    _pendingRequests[i].callback(_pendingRequests[i]);

                    if (_reposData[_pendingRequests[i].targetAddress].currentDownLoadRequest == _pendingRequests[i])
                    {
                        _reposData[_pendingRequests[i].targetAddress].currentDownLoadRequest = null;
                    }
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
            idwriter.WriteLine("\"{0}\" : \"{1}\"", "download", data.archiveURL);

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


    // ==== request callback 
    public void RetrievedRepoData(Request req)
    {
        var data = JSON.Parse(req.request.downloadHandler.text);

        RepoData repoData = new RepoData();
        repoData.address = req.targetAddress;
        repoData.name = data["name"] == null ? "" : ((JSONString)data["name"]);
        repoData.description = data["description"] == null ? "" : ((JSONString)data["description"]);

        repoData.archiveURL = data["archive_url"] == null ? "" : ((JSONString)data["archive_url"]);
        repoData.archiveURL = repoData.archiveURL.Replace("{archive_format}", "zipball");
        repoData.archiveURL = repoData.archiveURL.Replace("{/ref}", "");

        repoData.etag = req.request.GetResponseHeader("ETag");

        _reposData[repoData.address] = repoData;
    }
}
