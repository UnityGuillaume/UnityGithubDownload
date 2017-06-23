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



    public List<string> reposAddresses;

    protected List<Request> pendingRequests = new List<Request>();
    protected Dictionary<string, RepoData> _repoData = new Dictionary<string, RepoData>();

    //=================

    [MenuItem("Window/Github Lister")]
    static void Open()
    {
        var win = GetWindow<GithubLister>();

        win.maxSize = new Vector2(600, 600);
        win.Show();

        win.position = new Rect(100, 100, 600, 600);
    }

    //=================

    private void OnEnable()
    {
        cacheFilePath = Application.dataPath + "/../Library/GithubDownloaderCache";

        reposAddresses = new List<string>();

        reposAddresses.Add("UnityGuillaume/Prototypes");
        reposAddresses.Add("UnityGuillaume/packagedesigner");
        reposAddresses.Add("UnityGuillaume/ImporterRule");

        if (!DeserializeRepoData())
        {//should be change to doing it everytime, but checking against the etag of the repo
         //but conditional request don't seem to avoid reducing github request limit so for now just do it if the file don't exist
            RequestRepoData();
        }
    }

    private void OnDisable()
    {
        while (pendingRequests.Count > 0)
        {
            pendingRequests[0].request.Abort();
            pendingRequests.RemoveAt(0);
        }

        SerializeRepoData();
    }

    private void Update()
    {
        for (int i = 0; i < pendingRequests.Count; ++i)
        {
            if (pendingRequests[i].request.isDone)
            {
                if (pendingRequests[i].request.isError)
                {
                    Debug.LogError(pendingRequests[i].request.error);
                }
                else
                {
                    pendingRequests[i].callback(pendingRequests[i]);

                    if(_repoData[pendingRequests[i].targetAddress].currentDownLoadRequest == pendingRequests[i])
                    {
                        _repoData[pendingRequests[i].targetAddress].currentDownLoadRequest = null;
                    }
                }

                pendingRequests.RemoveAt(i);
                i--;
            }
        }
    }

    private void OnGUI()
    {
        foreach (var val in _repoData.Values)
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(val.address, EditorStyles.boldLabel);

            if (val.currentDownLoadRequest == null)
            {
                if (GUILayout.Button("Import", GUILayout.Width(64)))
                {
                    ImportRepo(val);
                }
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button(Mathf.FloorToInt(val.currentDownLoadRequest.request.downloadProgress * 100) + "%", GUILayout.Width(64));
                GUI.enabled = true;

                Repaint();
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.LabelField(val.description);

            GUILayout.Space(8);
        }
    }

    void ImportRepo(RepoData repo)
    {
        string correctedUrl = repo.archiveURL.Replace("{archive_format}", "zipball");
        correctedUrl = correctedUrl.Replace("{/ref}", "");

        Request req = new Request();
        req.request = UnityWebRequest.Get(correctedUrl);
        req.callback = RetrievePackage;
        req.targetAddress = repo.address;

        req.request.Send();

        repo.currentDownLoadRequest = req;
        pendingRequests.Add(req);
    }

    void RequestRepoData()
    {
        for (int i = 0; i < reposAddresses.Count; ++i)
        {
            Request req = new Request();
            req.targetAddress = reposAddresses[i];
            req.request = UnityWebRequest.Get("https://api.github.com/repos/" + reposAddresses[i]);
            //req.request.SetRequestHeader("If-Modified-Since", System.DateTime.UtcNow.ToString("ddd, dd MMM yyyy HH:mm:ss 'UTC'"));
            req.request.Send();
            req.callback = RetrievedRepoData;

            pendingRequests.Add(req);
        }
    }

    void RetrievePackage(Request req)
    {
        string baseFolder = Application.dataPath + "/" + _repoData[req.targetAddress].name;

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

    public void RetrievedRepoData(Request req)
    {
        var data = JSON.Parse(req.request.downloadHandler.text);

        RepoData repoData = new RepoData();
        repoData.address = req.targetAddress;
        repoData.name = data["name"] == null ? "" : ((JSONString)data["name"]);
        repoData.description = data["description"] == null ? "" : ((JSONString)data["description"]);
        repoData.archiveURL = data["archive_url"] == null ? "" : ((JSONString)data["archive_url"]);
        repoData.etag = req.request.GetResponseHeader("ETag");

        _repoData[repoData.address] = repoData;
    }

    void RetriedRateLimit(UnityWebRequest req)
    {
        Debug.Log(req.downloadHandler.text);
    }


    //-------------------

    string cacheFilePath;

    void SerializeRepoData()
    {
        FileStream file = new FileStream(cacheFilePath, FileMode.Create);
        BinaryWriter writer = new BinaryWriter(file);

        writer.Write(RepoData.version);
        writer.Write(_repoData.Count);
        foreach (var entry in _repoData.Values)
        {
            writer.Write(entry.etag);
            writer.Write(entry.address);
            writer.Write(entry.name);
            writer.Write(entry.archiveURL);
            writer.Write(entry.description);
        }

        writer.Close();
        file.Close();
    }

    bool DeserializeRepoData()
    {
        if (!File.Exists(cacheFilePath))
        {
            return false;
        }

        FileStream file = new FileStream(cacheFilePath, FileMode.Open);
        BinaryReader reader = new BinaryReader(file);

        int ver = reader.ReadInt32();
        if (ver != RepoData.version)
        {
            reader.Close();
            file.Close();
            return false;
        }

        int count = reader.ReadInt32();
        for (int i = 0; i < count; ++i)
        {
            RepoData data = new RepoData();

            data.etag = reader.ReadString();
            data.address = reader.ReadString();
            data.name = reader.ReadString();
            data.archiveURL = reader.ReadString();
            data.description = reader.ReadString();

            _repoData[data.address] = data;
        }

        reader.Close();
        file.Close();

        return true;
    }
}
