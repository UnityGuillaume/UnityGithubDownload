using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using SimpleJSON;
using ICSharpCode.SharpZipLib.Zip;

public class GithubLister : EditorWindow
{
    public class Request
    {
        public delegate void RequestFinished(Request req);

        //set to -1 if a generic request or to the index of the repo which started that request (for downloading)
        public int targetRepo;
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
        public Texture2D icone;

        public Request currentDownLoadRequest = null;
    }


    protected List<Request> pendingRequests = new List<Request>();
    protected List<RepoData> _repoData = new List<RepoData>();

    protected List<string> _categoryNames = new List<string>();
    protected Dictionary<string, List<int>> _categories = new Dictionary<string, List<int>>();

    protected int _currentIdx = 0;

    protected Vector2 _scrollValue = Vector2.zero;

    //=================

    [MenuItem("Window/Github Lister")]
    static void Open()
    {
        var win = GetWindow<GithubLister>();

        win.Show();

        win.position = new Rect(100, 100, 600, 600);
    }

    //=================

    private void OnEnable()
    {
        cacheFilePath = Application.dataPath + "/../Library/GithubDownloaderCache.json";

        _categories = new Dictionary<string, List<int>>();
        _categories["all"] = new List<int>();

        _categoryNames = new List<string>();
        _categoryNames.Add("all");

        FindRepoData();
    }

    private void OnDisable()
    {
        while (pendingRequests.Count > 0)
        {
            pendingRequests[0].request.Abort();
            pendingRequests.RemoveAt(0);
        }
    }

    private void Update()
    {
        for (int i = 0; i < pendingRequests.Count; ++i)
        {
            if (pendingRequests[i].request.isDone)
            {
                if (pendingRequests[i].request.isNetworkError)
                {
                    Debug.LogError(pendingRequests[i].request.error);
                }
                else
                {
                    pendingRequests[i].callback(pendingRequests[i]);

                    if(_repoData[pendingRequests[i].targetRepo].currentDownLoadRequest == pendingRequests[i])
                    {
                        _repoData[pendingRequests[i].targetRepo].currentDownLoadRequest = null;
                    }
                }

                pendingRequests.RemoveAt(i);
                i--;
            }
        }
    }

    private void OnGUI()
    {
        Color c = Handles.color;
        Handles.color = new Color(0.3f, 0.3f, 0.3f, 0.6f);

        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(128));

        for (int i = 0; i < _categoryNames.Count; ++i)
        {
            GUI.enabled = !(i == _currentIdx);
            if (GUILayout.Button(_categoryNames[i]))
                _currentIdx = i;
        }
        GUI.enabled = true;

        GUILayout.EndVertical();

        GUILayout.BeginHorizontal();
        _scrollValue = GUILayout.BeginScrollView(_scrollValue);
        for (int i = 0; i < _categories[_categoryNames[_currentIdx]].Count; ++i)
        {
            int idx = _categories[_categoryNames[_currentIdx]][i];
            RepoData val = _repoData[idx];

            GUILayout.BeginHorizontal();

            Rect iconeRect = GUILayoutUtility.GetRect(64, 64);
            if(val.icone != null)
                GUI.DrawTexture(iconeRect, val.icone, ScaleMode.ScaleToFit);

            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            if (val.currentDownLoadRequest == null)
            {
                if (GUILayout.Button("Import", GUILayout.Width(64)))
                {
                    ImportRepo(idx);
                }
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button(Mathf.FloorToInt(val.currentDownLoadRequest.request.downloadProgress * 100) + "%", GUILayout.Width(64));
                GUI.enabled = true;

                Repaint();
            }

            EditorGUILayout.LabelField(val.name, EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            EditorGUILayout.LabelField(val.description);

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            Rect r = GUILayoutUtility.GetLastRect();

            Handles.DrawLine(new Vector3(0, r.yMax), new Vector3(position.width, r.yMax));
            GUILayout.Space(8);
        }

        GUILayout.EndScrollView();

        GUILayout.EndVertical();

        GUILayout.EndHorizontal();

        Handles.color = c;
    }

    void FindRepoData()
    {
        if (!File.Exists(cacheFilePath))
        {
            if (File.Exists(Application.dataPath + "/../GithubDownloaderCache.json"))
            {//we are on a test installation, where the file was generated locally, just copy it over
                File.Copy(Application.dataPath + "/../GithubDownloaderCache.json", cacheFilePath);
                DeserializeRepoData();
            }
            else
                DownloadRepoList();
        }
        else
        {
            if ((System.DateTime.Now - File.GetCreationTime(cacheFilePath)).TotalDays > 1)
            {//download it if we have a library older than a day
                DownloadRepoList();
            }
            else
                DeserializeRepoData();
        }
    }

    void DownloadRepoList()
    {
        Request repoListDl = new Request();
        repoListDl.request = UnityWebRequest.Get("https://api.github.com/repos/UnityGuillaume/UnityGithubDownload/contents/GithubDownloaderCache.json");
        repoListDl.request.SetRequestHeader("accept", "accept:application/vnd.github.v3.raw");

        repoListDl.callback = RepoListDownloaded;
        repoListDl.request.Send();

        pendingRequests.Add(repoListDl);
    }

    void RepoListDownloaded(Request req)
    {
        File.WriteAllText(cacheFilePath, req.request.downloadHandler.text);
        DeserializeRepoData();
    }

    void ImportRepo(int index)
    {
        string correctedUrl = _repoData[index].archiveURL.Replace("{archive_format}", "zipball");
        correctedUrl = correctedUrl.Replace("{/ref}", "");

        Request req = new Request();
        req.request = UnityWebRequest.Get(correctedUrl);
        req.callback = RetrievePackage;
        req.targetRepo = index;

        req.request.Send();

        _repoData[index].currentDownLoadRequest = req;
        pendingRequests.Add(req);
    }

    void RetrievePackage(Request req)
    {
        string baseFolder = Application.dataPath + "/" + _repoData[req.targetRepo].name;

        if(!Directory.Exists(baseFolder))
        {
            Directory.CreateDirectory(baseFolder);
        }

        MemoryStream str = new MemoryStream(req.request.downloadHandler.data);
        ZipFile file = new ZipFile(str);

        foreach (ZipEntry ze in file)
        {
            if (!ze.IsFile)
            {
                continue;           // Ignore directories
            }

            string correctedFilename = ze.Name.Substring(ze.Name.IndexOf("/")+1);
            if (!correctedFilename.StartsWith("Assets/"))
                continue; //we ignore any file not in asset folder

            //remove the Assets/
            correctedFilename = correctedFilename.Substring(correctedFilename.IndexOf("/")+1);

            byte[] buffer = new byte[4096];     // 4K is optimum
            Stream zipStream = file.GetInputStream(ze);

            // Manipulate the output filename here as desired.
            string fullZipToPath = Path.Combine(baseFolder, correctedFilename);
            string directoryName = Path.GetDirectoryName(fullZipToPath);
            if (directoryName.Length > 0)
                Directory.CreateDirectory(directoryName);

            // Unzip file in buffered chunks. This is just as fast as unpacking to a buffer the full size
            // of the file, but does not waste memory.
            // The "using" will close the stream even if an exception occurs.
            using (FileStream streamWriter = File.Create(fullZipToPath))
            {
                CopyStream(zipStream, streamWriter, buffer);
            }
        }

        file.Close();
        str.Close();

        AssetDatabase.Refresh();
    }

    public static void CopyStream(Stream input, Stream output, byte[] buffer)
    {
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
        }
    }

    //-------------------

    string cacheFilePath;

    void DeserializeRepoData()
    {
        if (!File.Exists(cacheFilePath))
        {
            return;
        }

        string strData = File.ReadAllText(cacheFilePath);
        var data = JSON.Parse(strData);

        foreach(var c in data.Children)
        {
            if(c.IsArray)
            {
                JSONArray a = c.AsArray;
                for(int i = 0; i< a.Count; ++i)
                {
                    RepoData repData = new RepoData();
                    repData.name = a[i]["name"];
                    repData.description = a[i]["desc"];
                    repData.archiveURL = a[i]["download"];
                    repData.category = a[i]["category"];

                    _categories["all"].Add(_repoData.Count);

                    if(repData.category != "")
                    {
                        if (!_categories.ContainsKey(repData.category))
                        {
                            _categories[repData.category] = new List<int>();
                            _categoryNames.Add(repData.category);
                        }

                        _categories[repData.category].Add(_repoData.Count);
                    }

                    if (a[i]["icone"] == "")
                        repData.icone = null;
                    else
                    {
                        repData.icone = new Texture2D(0, 0);
                        repData.icone.LoadImage(System.Convert.FromBase64String(a[i]["icone"]));
                    }

                    _repoData.Add(repData);
                }
            }
        }
    }
}
