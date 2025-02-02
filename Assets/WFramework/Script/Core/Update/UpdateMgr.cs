﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class UpdateMgr : SingletonUnity<UpdateMgr>
{

    private bool initialize = false;

    private List<string> downloadFiles = new List<string>();
    private Dictionary<string, string> todownloadFiles  = new Dictionary<string, string>();
    /// <summary>
    /// 初始化游戏管理器
    /// </summary>
    void Awake()
    {
    }

    /// <summary>
    /// 初始化
    /// </summary>
    public void Init()
    {
        DontDestroyOnLoad(gameObject);  //防止销毁自己

        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        CheckExtractResource(); //释放资源
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void CheckExtractResource()
    {
        bool isExists = Directory.Exists(Util.DataPath) &&
         File.Exists(Util.DataPath + "files.txt");
        if (isExists || AppConst.DebugMode)
        {
            StartCoroutine(OnCheckUpdate());
            return;   //文件已经解压过了，自己可添加检查文件列表逻辑
        }
        StartCoroutine(OnExtractResource());    //启动释放协成 
    }
    /// <summary>
    /// 检测对比文件是否要更新
    /// </summary>
    IEnumerator OnCheckUpdate()
    {
        if (!AppConst.UpdateMode)
        {
            //ResManager.initialize(Facade.m_GameManager.OnResourceInited);
            yield break;
        }
        todownloadFiles.Clear();

        CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_CHECK, "开始检查更新"), this);

        string dataPath = Util.DataPath;  //数据目录
        string url = AppConst.WebUrl;
        string random = DateTime.Now.ToString("yyyymmddhhmmss");
        string listUrl = url + "files.txt?v=" + random;
        Debug.LogWarning("LoadUpdate---->>>" + listUrl);

        WWW www = new WWW(listUrl); yield return www;
        if (www.error != null)
        {
            string message = "下载版本信息失败!>files.txt";
            CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_CHECK, "下载版本信息失败!>files.txt"), this);
            yield break;
        }
        if (!Directory.Exists(dataPath))
        {
            Directory.CreateDirectory(dataPath);
        }
         File.WriteAllBytes(dataPath + "files.txt", www.bytes);

        string filesText = www.text;
        string[] files = filesText.Split('\n');

        for (int i = 0; i < files.Length; i++)
        {
            if (string.IsNullOrEmpty(files[i])) continue;
            string[] keyValue = files[i].Split('|');
            string f = keyValue[0];
            string localfile = (dataPath + f).Trim();
            string path = Path.GetDirectoryName(localfile);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            string fileUrl = url + keyValue[0] + "?v=" + random;
            bool canUpdate = !File.Exists(localfile);
            if (!canUpdate)
            {
                string remoteMd5 = keyValue[1].Trim();
                string localMd5 = Util.md5file(localfile);
                canUpdate = !remoteMd5.Equals(localMd5);
                if (canUpdate) File.Delete(localfile);
            }
            //本地缺少文件
            if (canUpdate)
            {
                todownloadFiles[fileUrl] = localfile;
            }
        }
        yield return new WaitForEndOfFrame();
        // "检查更新完成!!"
        CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_CHECK, "检查更新结束"), this);
        // 对比完成 是否去下载
        if (todownloadFiles.Count > 0)
        {
            StartCoroutine(OnUpdateResource());
        }
        else
        {
            OnResourceInited();
        }
    }
    /// <summary>
    /// 启动更新下载，这里只是个思路演示，此处可启动线程下载更新
    /// </summary>
    IEnumerator OnUpdateResource()
    {
        downloadFiles.Clear();

        CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_MESSAGE, "开始更新下载"), this);

        foreach(KeyValuePair<string,string> kv in todownloadFiles)
        {
            //这里都是资源文件，用线程下载
            BeginDownload(kv.Key, kv.Value);
            while (!(IsDownOK(kv.Value))) { yield return new WaitForEndOfFrame(); }
        }
       
        yield return new WaitForEndOfFrame();
       
        CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_MESSAGE, "下载更新结束"), this);
        OnResourceInited();
    }

    /// <summary>
    /// 资源初始化结束
    /// </summary>
    public void OnResourceInited()
    {
        //AssetBundleSyncMgr.Instance.Initialize(AppConst.AssetDir, delegate ()
        //{
        //    Debug.Log("Initialize OK!!!");
        //    this.OnInitialize();
        //});
        AssetBundleMgr.Instance.Initialize();
        OnInitialize();
    }

    void OnInitialize()
    {
        initialize = true;
        CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_FINISH), this);
    }
    /// <summary>
    /// 线程下载
    /// </summary>
    void BeginDownload(string url, string file)
    {     //线程下载
        object[] param = new object[2] { url, file };
        ThreadEvent ev = new ThreadEvent();
        ev.Key = NotiConst.UPDATE_DOWNLOAD;
        ev.evParams.AddRange(param);
        ThreadMgr.Instance.AddEvent(ev, OnThreadCompleted);   //线程下载
    }

    /// <summary>
    /// 线程完成
    /// </summary>
    /// <param name="data"></param>
    void OnThreadCompleted(NotiData data)
    {
        switch (data.evName)
        {
            case NotiConst.UPDATE_EXTRACT:  //解压一个完成
                //
                break;
            case NotiConst.UPDATE_DOWNLOAD: //下载一个完成
                downloadFiles.Add(data.evParam.ToString());
                float progress =  downloadFiles.Count * 1.00f / todownloadFiles.Count;
                CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_PROGRESS, progress), this);
                break;
            case NotiConst.UPDATE_PROGRESS: 
                string  speed  = data.evParam.ToString();
                CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_DONLOAD_SPEED, speed), this);
                break;
        }
    }

    /// <summary>
    /// 是否下载完成
    /// </summary>
    bool IsDownOK(string file)
    {
        return downloadFiles.Contains(file);
    }
    void OnUpdateFailed(string file)
    {
        string message = "更新失败!>" + file;
        CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_MESSAGE, message), this);
    }
    IEnumerator OnExtractResource()
    {
        string dataPath = Util.DataPath;  //数据目录
        string resPath = Util.AppContentPath(); //游戏包资源目录

        if (Directory.Exists(dataPath)) Directory.Delete(dataPath, true);
        Directory.CreateDirectory(dataPath);

        string infile = resPath + "files.txt";
        string outfile = dataPath + "files.txt";
        if (File.Exists(outfile)) File.Delete(outfile);

        string message = "正在解包文件:>files.txt";
        Debug.Log(infile);
        Debug.Log(outfile);
        if (Application.platform == RuntimePlatform.Android)
        {
            WWW www = new WWW(infile);
            yield return www;

            if (www.isDone)
            {
                File.WriteAllBytes(outfile, www.bytes);
            }
            yield return 0;
        }
        else File.Copy(infile, outfile, true);
        yield return new WaitForEndOfFrame();

        //释放所有文件到数据目录
        string[] files = File.ReadAllLines(outfile);
        foreach (var file in files)
        {
            string[] fs = file.Split('|');
            infile = resPath + fs[0];  //
            outfile = dataPath + fs[0];

            message = "正在解包文件:>" + fs[0];
            Debug.Log("正在解包文件:>" + infile);

            CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_MESSAGE, "正在解包文件"), this);

            string dir = Path.GetDirectoryName(outfile);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (Application.platform == RuntimePlatform.Android)
            {
                WWW www = new WWW(infile);
                yield return www;

                if (www.isDone)
                {
                    File.WriteAllBytes(outfile, www.bytes);
                }
                yield return 0;
            }
            else
            {
                if (File.Exists(outfile))
                {
                    File.Delete(outfile);
                }
                File.Copy(infile, outfile, true);
            }
            yield return new WaitForEndOfFrame();
        }
        message = "解包完成!!!";

        CEventDispatcher.Instance.dispatchEvent(new CEvent(CEventName.UPDATE_MESSAGE, message), this);

        yield return new WaitForSeconds(0.1f);

        message = string.Empty;
        //释放完成，开始启动更新资源
        StartCoroutine(OnUpdateResource());
    }

}
