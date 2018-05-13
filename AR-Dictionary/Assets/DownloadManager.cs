using UnityEngine;
using System;
using System.Collections;
 
public class DownloadManager : MonoBehaviour {
    private WWW wwwData;
    private static DownloadManager dm = null;
   
    // Use this for initialization
    void Start () {
        if( DownloadManager.dm == null )
            DownloadManager.dm = FindObjectOfType(typeof(DownloadManager)) as DownloadManager;     
    }
   
    // Update is called once per frame
    void Update () {
   
    }
   
    void OnApplicationQuit()
    {
        DownloadManager.dm = null;
    }
   
    public delegate void DownloadCallback(string data, string sError, string text);
   
    private IEnumerator WaitForDownload(DownloadCallback fn, string text)
    {
        Debug.Log("Yielding");     
        yield return wwwData;
        Debug.Log("Yielded");
        fn(wwwData.text, wwwData.error, text);  
    }
   
    private void StartDownload(string sURL, DownloadCallback fn, string text)
    {
        try
        {  
            wwwData = new WWW(sURL);           
            Debug.Log("Starting download.");
            //StartCoroutine("WaitForDownload", fn, text);
            StartCoroutine(WaitForDownload(fn, text));
        }
        catch(Exception e)
        {
            Debug.Log(e.ToString());
        }  
    }
   
    public static void Download(string sURL, DownloadCallback fn, string text)
    {
        dm.StartDownload(sURL, fn, text);
    }
}