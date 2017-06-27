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

        public Request currentDownLoadRequest = null;
    }


    protected List<Request> pendingRequests = new List<Request>();
    protected List<RepoData> _repoData = new List<RepoData>();

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

        DeserializeRepoData();
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
                if (pendingRequests[i].request.isError)
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
        for (int i = 0; i < _repoData.Count; ++i)
        {
            RepoData val = _repoData[i];

            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(val.name, EditorStyles.boldLabel);

            if (val.currentDownLoadRequest == null)
            {
                if (GUILayout.Button("Import", GUILayout.Width(64)))
                {
                    ImportRepo(i);
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

        var data = JSON.Parse(File.ReadAllText(cacheFilePath));

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

                    _repoData.Add(repData);
                }
            }
        }
    }
}
